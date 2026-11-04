// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// cspell:ignore FFFFFFFZ

using System.Globalization;
using System.Text.RegularExpressions;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Azure.Mcp.Tools.Monitor.Options.Traces;
using Azure.Mcp.Tools.Monitor.Services.Traces;
using Azure.Mcp.Tools.Monitor.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.Monitor.Commands.Traces;

public abstract class BaseTraceCompareCommand(
    ILogger logger,
    IMonitorTracesService service,
    ISubscriptionResolver subscriptionResolver,
    string analysis)
    : BaseTraceCommand<TraceCompareOptions, TraceQueryCommandResult>(subscriptionResolver)
{
    public override void ValidateOptions(TraceCompareOptions options, ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);
        void Reject(string message) => validationResult.AddError(message, message);
        if (options.Workspace?.IndexOfAny(['/', '\\', '?', '#']) >= 0)
        {
            Reject("--workspace must be a workspace resource name.");
        }
        ValidateWindow(options.StartTime, options.EndTime, "current", validationResult);
        if (options.CompareStartTime is not null || options.CompareEndTime is not null)
        {
            ValidateWindow(options.CompareStartTime, options.CompareEndTime, "comparison", validationResult);
        }
        TraceAnalysisValidator.ValidateAnalysisMaxResults(options.MaxResults, 10000, validationResult);
        foreach (string? query in new[] { options.Query, options.CompareQuery })
        {
            if (query is not null && (string.IsNullOrWhiteSpace(query) || query.Length > 100000))
            {
                Reject("Population queries must contain 1..100000 characters.");
            }
        }
        if (analysis == "critical-path")
        {
            ValidateSelection(options.TraceIds, options.Query, "current", validationResult);
            ValidateSelection(options.CompareTraceIds, options.CompareQuery, "comparison", validationResult);
            if (options.AggregateBy is { Length: > 3 }
                || options.AggregateBy?.Any(field => !TraceAnalyzeCriticalPathCommand.IsAggregationField(NormalizeField(field))) == true)
            {
                Reject("--aggregate-by accepts at most 3 valid field names.");
            }
            TraceAnalysisValidator.ValidateCriticalPathPercentiles(options.Percentiles, validationResult);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.Query))
            {
                Reject("--query is required.");
            }
            if (string.IsNullOrWhiteSpace(options.CompareQuery) && options.CompareStartTime is null)
            {
                Reject("Supply --compare-query or both comparison timestamps.");
            }
            if (options.TraceIds is not null || options.CompareTraceIds is not null || options.AggregateBy is not null
                || options.Percentiles is not null || options.ResolveTraceLinks is not null || options.IncludeSamples is not null
                || options.IncludeRollups is not null || options.ExcludeSubtreePrefixes is not null || options.DeprioritizeSubtreePrefixes is not null)
            {
                Reject("Trace IDs and critical-path settings are supported only by query analyze-compare-critical-path.");
            }
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context, TraceCompareOptions options, CancellationToken cancellationToken)
    {
        try
        {
            bool criticalPath = analysis == "critical-path";
            var request = new TraceComparisonRequest
            {
                Current = new()
                {
                    Query = options.Query,
                    Language = "KQL",
                    TimeRange = Window(options.StartTime, options.EndTime),
                    TraceId = options.TraceIds is null ? null : string.Join(",", options.TraceIds.Select(id => id.Trim())),
                    Options = new()
                    {
                        MaxResults = options.MaxResults ?? 10000,
                        ResolveTraceLinks = criticalPath ? options.ResolveTraceLinks ?? false : null,
                        IncludeSamples = criticalPath ? options.IncludeSamples ?? true : null,
                        IncludeRollups = criticalPath ? options.IncludeRollups ?? false : null,
                        ExcludeSubtreePrefixes = options.ExcludeSubtreePrefixes,
                        DeprioritizeSubtreePrefixes = options.DeprioritizeSubtreePrefixes,
                    },
                },
                CompareTo = new()
                {
                    Query = options.CompareQuery,
                    TimeRange = options.CompareStartTime is null ? null : Window(options.CompareStartTime, options.CompareEndTime!),
                    TraceId = options.CompareTraceIds is null ? null : string.Join(",", options.CompareTraceIds.Select(id => id.Trim())),
                },
            };
            string? aggregation = criticalPath
                ? TraceAnalyzeCriticalPathCommand.BuildCriticalPathQuery(options.AggregateBy?.Select(NormalizeField).ToArray(), options.Percentiles)
                : null;
            TraceQueryResult result = await service.ComparePopulationsAsync(
                options.Subscription!, options.ResourceGroup, options.Workspace, analysis, request,
                aggregation, options.Tenant, cancellationToken);
            context.Response.Results = ResponseResult.Create(new TraceQueryCommandResult(result),
                MonitorJsonContext.Default.TraceQueryCommandResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error comparing trace populations. Analysis: {Analysis}, Subscription: {Subscription}", analysis, options.Subscription);
            HandleException(context, ex);
        }
        return context.Response;
    }

    private static TraceQueryTimeRange Window(string start, string end) => new()
    {
        StartTime = ParseTime(start).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ", CultureInfo.InvariantCulture),
        EndTime = ParseTime(end).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ", CultureInfo.InvariantCulture),
    };

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static void ValidateWindow(string? start, string? end, string population, ValidationResult validation)
    {
        if (!DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startTime)
            || !DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var endTime))
        {
            string message = $"The {population} window requires both ISO 8601 timestamps.";
            validation.AddError(message, message);
            return;
        }
        TraceAnalysisValidator.ValidateAnalysisWindow(startTime, endTime, validation);
    }

    private static void ValidateSelection(string[]? ids, string? query, string population, ValidationResult validation)
    {
        string? error = null;
        if ((ids is null) == (query is null))
        {
            error = $"Supply trace IDs or a filter query for the {population} population, not both.";
        }
        else if (ids is not null && (ids.Length is < 1 or > 1000 || ids.Any(id =>
            !Guid.TryParse(id.Trim(), out _) && (id.Trim().Length != 32 || !id.Trim().All(Uri.IsHexDigit)))))
        {
            error = $"The {population} population requires 1..1000 trace IDs (32 hexadecimal characters or GUIDs).";
        }
        else if (query is not null)
        {
            try
            {
                string structure = LogSearchQueryValidator.BlankQuotedTextAndRejectComments(query);
                if (!Regex.IsMatch(structure.Split("$$", 2, StringSplitOptions.None)[0], @"^\s*Traces\s*(?:\||$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                    || Regex.Matches(structure, @"\|\s*(\w+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Any(match => match.Groups[1].Value != "where")
                    || structure.Split("$$", StringSplitOptions.None).Length > 2)
                {
                    error = $"The {population} selection query must start with Traces and contain only where filters and optionally one $$ span-membership block.";
                }
            }
            catch (CommandValidationException ex)
            {
                error = $"Invalid {population} selection query: {ex.Message}";
            }
        }
        if (error is not null)
        {
            validation.AddError(error, error);
        }
    }

    private static string NormalizeField(string field)
    {
        string value = field.Trim();
        return value.StartsWith("['", StringComparison.Ordinal) && value.EndsWith("']", StringComparison.Ordinal) ? value[2..^2] : value;
    }
}

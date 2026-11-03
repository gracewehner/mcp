// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

[CommandMetadata(
    Id = "86c3b5bc-4031-4254-a0eb-dc9c4a522ffc",
    Name = "analyze-top-attributes",
    Title = "Find Top Distinguishing Trace Store Attributes",
    Description = """
        Find top distinguishing attributes associated with latency, errors, retries, or deployment
        changes by comparing distributed-trace cohorts in Azure Monitor Workspace Trace Store. Selection traces match
        a field/operator/value inside the selection window; the baseline is the rest of the overall
        query/window. Returns ranked attributes and per-value cohort counts, not causal proof.
        Uses workspace read access. If status is Running, use monitor traces query resume with
        the returned operationId instead of resubmitting.
        """,
    OperationPlane = ToolOperationPlane.Data,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class TraceAnalyzeTopAttributesCommand(
    ILogger<TraceAnalyzeTopAttributesCommand> logger,
    IMonitorTracesService tracesService,
    ISubscriptionResolver subscriptionResolver)
    : BaseTraceCommand<TraceAnalyzeTopAttributesOptions, TraceQueryCommandResult>(subscriptionResolver)
{
    public override void ValidateOptions(TraceAnalyzeTopAttributesOptions options, ValidationResult validationResult)
    {
        base.ValidateOptions(options, validationResult);
        TraceAnalysisValidator.ValidateAnalysisWindow(options.OverallStartTime, options.OverallEndTime, validationResult);
        TraceAnalysisValidator.ValidateAnalysisWindow(options.SelectionStartTime, options.SelectionEndTime, validationResult);
        TraceAnalysisValidator.ValidateAnalysisMaxResults(options.MaxResults, 10000, validationResult);
        if (options.SelectionStartTime < options.OverallStartTime || options.SelectionEndTime > options.OverallEndTime)
        {
            validationResult.AddError("The selection window must be contained within the overall window.", "Invalid selection window.");
        }
        if (!TraceAnalysisValidator.IsAnalysisIdentifier(options.SelectionField))
        {
            validationResult.AddError("--selection-field must be a bare identifier using letters, digits, underscores, and dots.", "Invalid selection-field.");
        }
        if (options.SelectionOp is not (">" or ">=" or "<" or "<=" or "==" or "!="))
        {
            validationResult.AddError("--selection-op must be >, >=, <, <=, ==, or !=.", "Invalid selection-op.");
        }
        if (!IsLiteral(options.SelectionValue))
        {
            validationResult.AddError("--selection-value must be a finite number or a single-quoted string literal.", "Invalid selection-value.");
        }
        if (options.TopN is < 1 or > 100)
        {
            validationResult.AddError("--top-n must be between 1 and 100.", "Invalid top-n.");
        }
        if (options.OverallQuery is { Length: > 65536 })
        {
            validationResult.AddError("--overall-query must not exceed 65536 characters.", "Oversized overall-query.");
        }
    }

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context, TraceAnalyzeTopAttributesOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var query = string.IsNullOrWhiteSpace(options.OverallQuery) ? "Traces" : options.OverallQuery.Trim();
            // A standalone span filter needs a trace-level pipeline for the compare stage.
            if (query.StartsWith("<<", StringComparison.Ordinal))
            {
                query = "Traces $$ " + query;
            }
            query += FormattableString.Invariant(
                $" | compare({options.SelectionField} {options.SelectionOp} {options.SelectionValue}, {options.TopN ?? 8}, {options.SelectionStartTime.UtcDateTime:O}, {options.SelectionEndTime.UtcDateTime:O})");
            var result = await tracesService.CompareAsync(
                options.Subscription!, options.ResourceGroup, options.Workspace, query,
                options.OverallStartTime, options.OverallEndTime, options.MaxResults ?? 10000,
                options.Tenant, cancellationToken);
            context.Response.Results = ResponseResult.Create(
                new TraceQueryCommandResult(result), MonitorJsonContext.Default.TraceQueryCommandResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error comparing distributed traces. Subscription: {Subscription}", options.Subscription);
            HandleException(context, ex);
        }
        return context.Response;
    }

    private static bool IsLiteral(string? value) =>
        value is { Length: > 0 and <= 4096 } &&
        ((double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        || Regex.IsMatch(value, @"\A'(?:[^'\\]|''|\\['\\])*'\z", RegexOptions.NonBacktracking));
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Core;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Azure.Mcp.Tools.Monitor.Validation;

namespace Azure.Mcp.Tools.Monitor.Services.Traces;

public sealed partial class MonitorTracesService
{
    private const string CompareStatisticsEndpointPath = "traces:compare-statistics";
    private const string CompareAttributesEndpointPath = "traces:compare-attributes";
    private const string CompareCriticalPathEndpointPath = "traces:compare-critical-path";

    public async Task<TraceQueryResult> ComparePopulationsAsync(
        string subscription,
        string resourceGroup,
        string workspace,
        string analysis,
        TraceComparisonRequest request,
        string? criticalPathQuery = null,
        string? tenant = null,
        CancellationToken cancellationToken = default)
    {
        string endpointPath = analysis switch
        {
            TraceComparisonAnalysis.Statistics => CompareStatisticsEndpointPath,
            TraceComparisonAnalysis.Attributes => CompareAttributesEndpointPath,
            TraceComparisonAnalysis.CriticalPath => CompareCriticalPathEndpointPath,
            _ => throw new ArgumentException("Unknown trace comparison operation.", nameof(analysis)),
        };

        ValidateRequiredParameters(
            (nameof(subscription), subscription), (nameof(resourceGroup), resourceGroup), (nameof(workspace), workspace));
        var session = await OpenSessionAsync(subscription, resourceGroup, workspace, tenant, cancellationToken);
        // CompareTo's body accepts the workspace account ID, not the longer ARM resource ID.
        request.TraceAccountId = session.AccountId is { Length: > 0 and <= 100 } accountId
            && !string.IsNullOrWhiteSpace(accountId) ? accountId
            : throw new InvalidOperationException("The workspace does not publish a valid accountId required for trace comparisons.");
        if (analysis == TraceComparisonAnalysis.CriticalPath)
        {
            request.Current.TraceId = await ResolveComparisonIdsAsync(
                session, request.Current, request.Current.TimeRange!, cancellationToken);
            request.CompareTo.TraceId = await ResolveComparisonIdsAsync(
                session, request.CompareTo, request.CompareTo.TimeRange ?? request.Current.TimeRange!, cancellationToken);
            // Selection queries discover IDs; both CPA populations must retain the same aggregation.
            request.Current.Query = criticalPathQuery;
            request.CompareTo.Query = null;
        }

        using var content = RequestContent.Create(
            JsonSerializer.SerializeToUtf8Bytes(request, MonitorJsonContext.Default.TraceComparisonRequest));
        string operationId = await SendSubmitAsync(session, endpointPath, content, cancellationToken);

        // maxResults selects traces, not joined attribute rows; don't truncate the comparative payload.
        return await PollForResultsAsync(session, operationId, 1000, int.MaxValue, false, cancellationToken);
    }

    private async Task<string> ResolveComparisonIdsAsync(
        TraceSession session,
        TraceComparisonPopulation population,
        TraceQueryTimeRange timeRange,
        CancellationToken cancellationToken)
    {
        if (population.TraceId is not null)
        {
            return population.TraceId;
        }

        string query = population.Query!;
        int separator = LogSearchQueryValidator.BlankQuotedTextAndRejectComments(query).IndexOf("$$", StringComparison.Ordinal);
        string discovery = separator < 0 ? query + " | project trace_id"
            : query[..separator].TrimEnd() + " | project trace_id " + query[separator..];
        string operationId = await SendSubmitAsync(session, discovery,
            DateTimeOffset.Parse(timeRange.StartTime, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(timeRange.EndTime, System.Globalization.CultureInfo.InvariantCulture),
            new TraceQueryOptionsBody { MaxResults = 10000 }, QueryEndpointPath, null, cancellationToken);
        TraceQueryResult result = await PollForResultsAsync(session, operationId, 1000, 10000, false, cancellationToken);
        if (result.Status != "Succeeded" || result.OutputLimitReached || result.Payload is not { } payload
            || !payload.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() >= 10000
            || IsPartialComparisonDiscovery(payload))
        {
            throw new InvalidOperationException(
                $"Critical-path population discovery is capped, partial or incomplete (operation {operationId}). "
                + "Narrow the query/window and retry; no sampled comparison was submitted.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("trace_id", out var value)
                || value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } traceId)
            {
                throw new InvalidOperationException("Critical-path population discovery returned an invalid trace ID.");
            }
            if (Guid.TryParse(traceId, out _))
            {
                ids.Add(traceId);
                continue;
            }
            if (traceId.Length > 32 || !traceId.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException("Critical-path population discovery returned an invalid trace ID.");
            }
            // Hex projections may omit leading zeroes; CPA requires the complete 16-byte ID.
            ids.Add(traceId.PadLeft(32, '0'));
        }
        if (ids.Count is < 1 or > 1000)
        {
            throw new InvalidOperationException("Critical-path comparison requires 1..1000 distinct trace IDs per population; narrow the query/window.");
        }
        return string.Join(",", ids);
    }

    private static bool IsPartialComparisonDiscovery(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        static bool HasPartialFlag(IEnumerable<JsonProperty> properties) =>
            properties.Any(property =>
                property.Name is "hasLimitReached" or "isPartialResult" or "isSystemLimitHit"
                && property.Value.ValueKind == JsonValueKind.True);

        if (HasPartialFlag(element.EnumerateObject()))
        {
            return true;
        }
        return element.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && HasPartialFlag(metadata.EnumerateObject());
    }
}

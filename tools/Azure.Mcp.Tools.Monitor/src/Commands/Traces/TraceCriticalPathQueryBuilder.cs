// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Commands.Traces;

internal static class TraceCriticalPathQueryBuilder
{
    public static string Build(string[]? aggregateBy, int[]? percentiles)
    {
        var dimensions = aggregateBy is { Length: > 0 } ? aggregateBy : ["span.name"];
        var values = percentiles is { Length: > 0 } ? percentiles : [50, 95, 99];
        var clauses = values.Select(p => FormattableString.Invariant($"p{p}=percentile(duration_nano, {p})"));
        return $"Traces | summarize {string.Join(", ", clauses)} by {string.Join(", ", dimensions.Select(d => $"['{d}']"))}";
    }
}

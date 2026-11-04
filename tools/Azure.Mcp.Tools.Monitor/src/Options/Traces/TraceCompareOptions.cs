// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.Monitor.Options.Traces;

public sealed class TraceCompareOptions()
    : ITraceCompareOptions
{
    [Option(Description = "Required current population KQL. For statistics, use matching named aggregations and grouping keys in both queries.")]
    public required string Query { get; set; }

    [Option(Description = "Current population start time, UTC ISO 8601. Each population window must be positive and no longer than one day.")]
    public required string StartTime { get; set; }

    [Option(Description = "Current population end time, UTC ISO 8601.")]
    public required string EndTime { get; set; }

    [Option(Description = "Independent comparison KQL. For statistics and attributes, omit to reuse the current query over --compare-start-time/--compare-end-time. Critical-path requires a filters-only query or --compare-trace-ids, not both.")]
    public string? CompareQuery { get; set; }

    [Option(Description = "Comparison start time. Supply both comparison timestamps or neither; omission inherits the current window.")]
    public string? CompareStartTime { get; set; }

    [Option(Description = "Comparison end time, UTC ISO 8601.")]
    public string? CompareEndTime { get; set; }

    [Option(Description = "Trace sample size per population. Defaults to 10000 and is clamped to 1..10000. This is not the number of returned comparison rows.")]
    public int? MaxResults { get; set; }

    [Option(Description = "The Azure Monitor Workspace resource name enabled for Trace Store.")]
    public required string Workspace { get; set; }

    [Option(Description = OptionDescriptions.ResourceGroup)]
    public required string ResourceGroup { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }
}

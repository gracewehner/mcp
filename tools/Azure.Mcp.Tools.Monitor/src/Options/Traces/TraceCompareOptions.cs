// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.Monitor.Options.Traces;

public sealed class TraceCompareOptions()
    : ISubscriptionOption
{
    [Option(Description = "Current population KQL, required for statistics and attributes. For statistics, use matching named aggregations and grouping keys in both queries. For critical-path, supply a filters-only Traces query or --trace-ids, not both.")]
    public string? Query { get; set; }

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

    [Option(Description = "Trace sample size per population. Default 10000; range 1..10000. This is not the number of returned comparison rows.")]
    public int? MaxResults { get; set; }

    [Option(Description = "Critical-path only: 1..1000 explicit current trace IDs (32 hexadecimal characters or GUIDs), instead of --query.")]
    public string[]? TraceIds { get; set; }

    [Option(Description = "Critical-path only: 1..1000 explicit comparison trace IDs, instead of --compare-query.")]
    public string[]? CompareTraceIds { get; set; }

    [Option(Description = "Critical-path only: grouping fields. Default span.name; maximum 3. Accepts bare or bracket-quoted field names.")]
    public string[]? AggregateBy { get; set; }

    [Option(Description = "Critical-path only: up to 5 percentiles, each 1..99. Default 50, 95, 99.")]
    public int[]? Percentiles { get; set; }

    [Option(Description = "Critical-path only: resolve linked traces. Default false.")]
    public bool? ResolveTraceLinks { get; set; }

    [Option(Description = "Critical-path only: include sample trace IDs per aggregation row. Default true.")]
    public bool? IncludeSamples { get; set; }

    [Option(Description = "Critical-path only: include roll-up rows. Default false.")]
    public bool? IncludeRollups { get; set; }

    [Option(Description = "Critical-path only: span-name prefixes excluded with their subtrees.")]
    public string[]? ExcludeSubtreePrefixes { get; set; }

    [Option(Description = "Critical-path only: span-name prefixes given lower priority when concurrent work exists.")]
    public string[]? DeprioritizeSubtreePrefixes { get; set; }

    [Option(Description = "The Azure Monitor Workspace resource name enabled for Trace Store.")]
    public required string Workspace { get; set; }

    [Option(Description = OptionDescriptions.ResourceGroup)]
    public required string ResourceGroup { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }
}

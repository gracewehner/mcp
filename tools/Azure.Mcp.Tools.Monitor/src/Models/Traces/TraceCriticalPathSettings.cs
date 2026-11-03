// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Models.Traces;

public sealed class TraceCriticalPathSettings
{
    public required string[] TraceIds { get; init; }
    public bool ResolveTraceLinks { get; init; }
    public bool IncludeSamples { get; init; } = true;
    public bool IncludeRollups { get; init; }
    public string[]? ExcludeSubtreePrefixes { get; init; }
    public string[]? DeprioritizeSubtreePrefixes { get; init; }
}

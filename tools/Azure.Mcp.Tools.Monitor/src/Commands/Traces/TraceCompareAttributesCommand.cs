// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Azure.Mcp.Tools.Monitor.Options.Traces;
using Azure.Mcp.Tools.Monitor.Services.Traces;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;

namespace Azure.Mcp.Tools.Monitor.Commands.Traces;

[CommandMetadata(Id = "233772b7-ef6e-459e-a657-2d7c8414d42a", Name = "analyze-compare-attributes", Title = "Compare Trace Attributes",
    Description = """
        Compare attribute distributions for two independently filtered distributed-trace populations.
        Use for before/after deployments, different services or non-overlapping time windows.
        This calls traces:compare-attributes, not the selection-based traces:compare top-attributes operation.
        Returns all backend comparative rows, scores and coverage metadata; max-results selects traces per population,
        not attribute rows. Requires subscription, resource-group, workspace, query, start-time, end-time
        and compare-query or both comparison timestamps. Omitted comparison fields inherit the current population.
        Continue a running operation with monitor traces query resume using its operationId; never resubmit.
        Azure public cloud only; the caller needs workspace read access. Feature-disabled API errors surface directly.
        """,
    OperationPlane = ToolOperationPlane.Data, Destructive = false, Idempotent = true, OpenWorld = false,
    ReadOnly = true, Secret = false, LocalRequired = false)]
public sealed class TraceCompareAttributesCommand(
    ILogger<TraceCompareAttributesCommand> logger,
    IMonitorTracesService service,
    ISubscriptionResolver subscriptionResolver)
    : BaseTraceCompareCommand<TraceCompareOptions>(logger, service, subscriptionResolver, TraceComparisonAnalysis.Attributes);

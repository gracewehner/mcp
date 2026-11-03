// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.Monitor.Commands.Traces;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Azure.Mcp.Tools.Monitor.Services.Traces;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.Traces;

public sealed class TraceCompareAttributesCommandTests()
    : SubscriptionCommandUnitTestsBase<TraceCompareAttributesCommand, IMonitorTracesService>
{
    [Fact]
    public async Task UsesIndependentAttributeEndpointNotSelectionCompare()
    {
        Service.ComparePopulationsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), "attributes",
            Arg.Any<TraceComparisonRequest>(), null, Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(new TraceQueryResult { Status = "Succeeded", OperationId = "operation" });
        var response = await ExecuteCommandAsync("--subscription sub --resource-group rg --workspace amw --query Traces --compare-query Traces --start-time 2026-01-02T00:00:00Z --end-time 2026-01-02T01:00:00Z");
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Single(Service.ReceivedCalls());
    }
}

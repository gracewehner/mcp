// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Commands.Traces;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Azure.Mcp.Tools.Monitor.Services.Traces;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.Traces;

public sealed class TraceAnalyzeCriticalPathCommandTests : SubscriptionCommandUnitTestsBase<TraceAnalyzeCriticalPathCommand, IMonitorTracesService>
{
    private const string Id = "abcdef1234567890abcdef1234567890";
    private const string Target = "--subscription sub --resource-group rg --workspace amw --tenant tenant";
    private const string Window = "--start-time 2026-10-12T00:00:00Z --end-time 2026-10-12T01:00:00Z";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_BuildsAggregationAndForwardsSettings(bool custom)
    {
        Service.CriticalPathAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string[]>(), Arg.Any<TraceQueryOptionsBody>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TraceQueryResult { Status = "Running", OperationId = "op-critical" });

        var args = custom
            ? "--aggregate-by span.service.name span.name span.attributes.guid:x-request-id --percentiles 90 99 --exclude-subtree-prefixes sql. redis. --deprioritize-subtree-prefixes background. --resolve-trace-links true --include-samples false --include-rollups true --max-results 1000"
            : "";
        var response = await ExecuteCommandAsync($"{Target} {Window} --trace-ids {Id} {args}");
        var result = ValidateAndDeserializeResponse(response, MonitorJsonContext.Default.TraceQueryCommandResult);

        Assert.Equal("Running", result.Result.Status);
        Assert.Equal("op-critical", result.Result.OperationId);
        var expectedQuery = custom
            ? "Traces | summarize p90=percentile(duration_nano, 90), p99=percentile(duration_nano, 99) by ['span.service.name'], ['span.name'], ['span.attributes.guid:x-request-id']"
            : "Traces | summarize p50=percentile(duration_nano, 50), p95=percentile(duration_nano, 95), p99=percentile(duration_nano, 99) by ['span.name']";
        await Service.Received(1).CriticalPathAsync(
            "sub", "rg", "amw", expectedQuery, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            Arg.Is<string[]>(ids => ids.SequenceEqual(new[] { Id })),
            Arg.Is<TraceQueryOptionsBody>(settings => settings.MaxResults == (custom ? 1000 : 10)
                && settings.IncludeSamples == !custom && settings.IncludeRollups == custom
                && settings.ResolveTraceLinks == custom
                && (!custom || (settings.ExcludeSubtreePrefixes!.SequenceEqual(new[] { "sql.", "redis." })
                    && settings.DeprioritizeSubtreePrefixes!.SequenceEqual(new[] { "background." })))),
            "tenant", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--trace-ids invalid")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --aggregate-by \"span.name'] | project trace_id\"")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --aggregate-by \"span.attributes.key'\"")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --aggregate-by \"span.attributes.key\\\"")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --aggregate-by a b c d")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --percentiles 0")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --percentiles 100")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --percentiles 1 2 3 4 5 6")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --max-results 1001")]
    [InlineData("--trace-ids abcdef1234567890abcdef1234567890 --exclude-subtree-prefixes \" \"")]
    [InlineData("")]
    public async Task ExecuteAsync_RejectsInvalidArguments(string args)
    {
        var response = await ExecuteCommandAsync($"{Target} {Window} {args}");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Empty(Service.ReceivedCalls());
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task ExecuteAsync_EnforcesTraceIdCount(int count, bool valid)
    {
        Service.CriticalPathAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<string[]>(), Arg.Any<TraceQueryOptionsBody>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new TraceQueryResult { Status = "Succeeded", OperationId = "op-critical" });

        var response = await ExecuteCommandAsync($"{Target} {Window} --trace-ids {string.Join(" ", Enumerable.Repeat(Id, count))}");
        Assert.Equal(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.Status);
        if (!valid)
        {
            Assert.Empty(Service.ReceivedCalls());
        }
    }
}

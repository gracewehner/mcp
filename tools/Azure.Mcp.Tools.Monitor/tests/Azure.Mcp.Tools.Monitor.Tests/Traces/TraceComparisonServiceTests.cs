// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.Traces;

public sealed class TraceComparisonServiceTests()
{
    private const string Id = "0123456789abcdef0123456789abcdef";
    private const string ComparisonId = "fedcba9876543210fedcba9876543210";

    [Theory]
    [InlineData("", "empty")]
    [InlineData("  ", "empty")]
    [InlineData("{", "malformed JSON")]
    [InlineData("[]", "operationId")]
    [InlineData("null", "operationId")]
    [InlineData("{}", "operationId")]
    [InlineData("""{"operationId":42}""", "operationId")]
    [InlineData("""{"operationId":" "}""", "operationId")]
    public async Task Submissions_RejectInvalidResponseBeforePolling(string body, string message)
    {
        foreach (bool comparison in new[] { false, true })
        {
            using var handler = new MonitorTracesTestHandler(1, false, submitBody: body);
            var (service, _) = MonitorTracesServiceTests.CreateService(handler);
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => comparison
                ? service.ComparePopulationsAsync(
                    MonitorTracesTestHandler.Subscription, "rg", "amw0", "statistics", Request(false),
                    cancellationToken: TestContext.Current.CancellationToken)
                : service.RunQueryAsync(
                    MonitorTracesTestHandler.Subscription, "rg", "amw0", "Traces",
                    DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, 10, false,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
            Assert.Equal(1, handler.SubmitCalls);
            Assert.Equal(0, handler.PollCalls);
        }
    }

    [Theory]
    [InlineData("statistics")]
    [InlineData("attributes")]
    [InlineData("critical-path")]
    public async Task Comparison_UsesDedicatedEnvelopeAndPreservesAllJoinedRows(string analysis)
    {
        using var first = Page("""{"value":[{"current":{"count":5},"compareTo":null,"status":"currentOnly"}],"nextLink":"ignored","metadata":{"current.isPartialResult":true,"compareTo.totalTraces":452}}""");
        using var second = Page("""{"value":[{"current":null,"compareTo":{"count":450},"status":"compareToOnly"}],"metadata":{"compareTo.totalTraces":452}}""");
        using var handler = new MonitorTracesTestHandler(1, false, resultPages: [first, second]);
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        var request = Request(analysis == "critical-path");
        request.Current.Options = new() { MaxResults = 1 };

        var result = await service.ComparePopulationsAsync(MonitorTracesTestHandler.Subscription, "rg", "amw0",
            analysis, request, "Traces | summarize p99=percentile(duration_nano, 99) by ['span.name']",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal($"/traces:compare-{analysis}", handler.SubmitUris.Single().AbsolutePath);
        Assert.Contains("api-version=2026-01-10-preview", handler.SubmitUris[0].Query);
        using var body = JsonDocument.Parse(handler.LastSubmitBody!);
        var current = body.RootElement.GetProperty("current");
        var comparison = body.RootElement.GetProperty("compareTo");
        Assert.Equal(MonitorTracesTestHandler.TraceAccount, body.RootElement.GetProperty("traceAccountId").GetString());
        Assert.Equal("KQL", current.GetProperty("language").GetString());
        Assert.False(current.TryGetProperty("traceAccountId", out _));
        Assert.Equal(1, current.GetProperty("options").GetProperty("maxResults").GetInt32());
        Assert.Equal("2026-01-02T00:00:00Z", current.GetProperty("timeRange").GetProperty("startTime").GetString());
        Assert.Equal("2026-01-01T00:00:00Z", comparison.GetProperty("timeRange").GetProperty("startTime").GetString());
        if (analysis == "critical-path")
        {
            Assert.Equal(Id, current.GetProperty("traceId").GetString());
            Assert.Equal(ComparisonId, comparison.GetProperty("traceId").GetString());
            Assert.False(comparison.TryGetProperty("query", out _));
        }
        else
        {
            Assert.Equal("Traces | where error_rate > 0", comparison.GetProperty("query").GetString());
        }
        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(2, result.Payload!.Value.GetProperty("value").GetArrayLength());
        Assert.True(result.Payload.Value.GetProperty("metadata").GetProperty("current.isPartialResult").GetBoolean());
        Assert.Equal(452, result.Payload.Value.GetProperty("metadata").GetProperty("compareTo.totalTraces").GetInt32());
        Assert.False(result.OutputLimitReached);

        await service.ResumeQueryAsync(MonitorTracesTestHandler.Subscription, "rg", "amw0", result.OperationId!,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.SubmitCalls);
    }

    [Theory]
    [InlineData("statistics")]
    [InlineData("attributes")]
    public async Task Comparison_OmitsInheritedQueryAndWindow(string analysis)
    {
        using var handler = new MonitorTracesTestHandler(1, false);
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        var request = Request(false);
        request.CompareTo = new() { Query = "Traces" };
        await service.ComparePopulationsAsync(MonitorTracesTestHandler.Subscription, "rg", "amw0",
            analysis, request, cancellationToken: TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(handler.LastSubmitBody!);
        Assert.False(body.RootElement.GetProperty("compareTo").TryGetProperty("timeRange", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(101)]
    public async Task Comparison_RejectsInvalidAccountIdBeforeDataPlaneSubmit(int length)
    {
        using var handler = new MonitorTracesTestHandler(1, false,
            accountId: new string(length == 1 ? ' ' : 'a', length));
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ComparePopulationsAsync(
            MonitorTracesTestHandler.Subscription, "rg", "amw0", "statistics", Request(false),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.DataPlaneCalls);
    }

    [Theory]
    [InlineData("false")]
    public async Task CriticalPath_DiscoveryDeduplicatesAndPadsIdsWithoutReplacingAggregation(string flag)
    {
        using var current = Page($$$$"""{"value":[{"trace_id":"a"},{"trace_id":"a"}],"metadata":{"hasLimitReached":{{{{flag}}}}}}""");
        using var comparison = Page("""{"value":[{"trace_id":"b"}]}""");
        using var handler = new MonitorTracesTestHandler(1, false, resultPages: [current, comparison]);
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        var request = Request(false);
        request.Current.Query = "Traces | where duration_nano > 10 $$ <<Traces | where ['span.name'] == 'GET'>>";
        const string aggregation = "Traces | summarize p99=percentile(duration_nano, 99) by ['span.name']";
        await service.ComparePopulationsAsync(MonitorTracesTestHandler.Subscription, "rg", "amw0",
            "critical-path", request, aggregation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.SubmitCalls);
        using var discovery = JsonDocument.Parse(handler.SubmitBodies[0]);
        Assert.Equal("Traces | where duration_nano > 10 | project trace_id $$ <<Traces | where ['span.name'] == 'GET'>>",
            discovery.RootElement.GetProperty("query").GetString());
        Assert.Equal(10000, discovery.RootElement.GetProperty("options").GetProperty("maxResults").GetInt32());
        using var body = JsonDocument.Parse(handler.SubmitBodies[2]);
        Assert.Equal(aggregation, body.RootElement.GetProperty("current").GetProperty("query").GetString());
        Assert.Equal(new string('0', 31) + "a", body.RootElement.GetProperty("current").GetProperty("traceId").GetString());
        Assert.Equal(new string('0', 31) + "b", body.RootElement.GetProperty("compareTo").GetProperty("traceId").GetString());
        Assert.False(body.RootElement.GetProperty("compareTo").TryGetProperty("query", out _));
    }

    [Theory]
    [InlineData("""{"value":[{"trace_id":"a"}],"isPartialResult":true}""")]
    [InlineData("""{"value":[{"trace_id":"a"}],"metadata":{"isSystemLimitHit":true}}""")]
    [InlineData("""{"value":[{"trace_id":"a"}],"metadata":{"hasLimitReached":true}}""")]
    [InlineData("""{"value":[]}""")]
    [InlineData("""{"value":[{"trace_id":"invalid"}]}""")]
    public async Task CriticalPath_RejectsIncompleteOrInvalidDiscoveryBeforeComparison(string page)
    {
        using var response = Page(page);
        using var nextPopulation = Page("""{"value":[{"trace_id":"b"}]}""");
        using var handler = new MonitorTracesTestHandler(1, false, resultPages: [response, nextPopulation]);
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ComparePopulationsAsync(
            MonitorTracesTestHandler.Subscription, "rg", "amw0", "critical-path", Request(false), "Traces | summarize count=count() by ['span.name']",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.SubmitCalls);
        Assert.Equal("/traces:query", handler.SubmitUris[0].AbsolutePath);
    }

    [Theory]
    [InlineData(1001, true)]
    [InlineData(10000, false)]
    public async Task CriticalPath_RejectsOversizedOrCappedDiscovery(int rowCount, bool distinct)
    {
        string rows = string.Join(",", Enumerable.Range(0, rowCount).Select(index =>
            $$"""{"trace_id":"{{(distinct ? index : 0).ToString("x32", System.Globalization.CultureInfo.InvariantCulture)}}" }"""));
        using var page = Page($$"""{"value":[{{rows}}]}""");
        using var handler = new MonitorTracesTestHandler(1, false, resultPages: [page]);
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ComparePopulationsAsync(
            MonitorTracesTestHandler.Subscription, "rg", "amw0", "critical-path", Request(false), "Traces",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(handler.SubmitUris);
        Assert.Equal("/traces:query", handler.SubmitUris[0].AbsolutePath);
    }

    private static HttpResponseMessage Page(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Theory]
    [InlineData("Traces | where root_span_name == '$$'", "Traces | where root_span_name == '$$' | project trace_id")]
    [InlineData("Traces | where root_span_name == 'it''s|POST$$' $$ <<Traces | where ['span.name'] == @'it''s|POST$$'>>",
        "Traces | where root_span_name == 'it''s|POST$$' | project trace_id $$ <<Traces | where ['span.name'] == @'it''s|POST$$'>>")]
    public async Task CriticalPath_DiscoveryPreservesQuotedSeparatorsForBothPopulations(string query, string expected)
    {
        using var current = Page("""{"value":[{"trace_id":"a"}]}""");
        using var comparison = Page("""{"value":[{"trace_id":"b"}]}""");
        using var handler = new MonitorTracesTestHandler(1, false, resultPages: [current, comparison]);
        var (service, _) = MonitorTracesServiceTests.CreateService(handler);
        var request = new TraceComparisonRequest
        {
            Current = new()
            {
                Query = query,
                TimeRange = new() { StartTime = "2026-01-02T00:00:00.1Z", EndTime = "2026-01-02T00:00:00.9Z" },
                Options = new() { MaxResults = 10000 },
            },
            CompareTo = new()
            {
                Query = query,
                TimeRange = new() { StartTime = "2026-01-01T00:00:00.1Z", EndTime = "2026-01-01T00:00:00.9Z" },
            },
        };
        await service.ComparePopulationsAsync(MonitorTracesTestHandler.Subscription, "rg", "amw0",
            "critical-path", request, "Traces | summarize count=count() by ['span.name']",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, handler.SubmitBodies.Count);
        for (int index = 0; index < 2; index++)
        {
            using var body = JsonDocument.Parse(handler.SubmitBodies[index]);
            var expectedWindow = index == 0 ? request.Current.TimeRange : request.CompareTo.TimeRange;
            Assert.Equal(expected, body.RootElement.GetProperty("query").GetString());
            Assert.Equal(expectedWindow!.StartTime, body.RootElement.GetProperty("timeRange").GetProperty("startTime").GetString());
            Assert.Equal(expectedWindow.EndTime, body.RootElement.GetProperty("timeRange").GetProperty("endTime").GetString());
        }
    }

    private static TraceComparisonRequest Request(bool criticalPath) => new()
    {
        Current = new()
        {
            Query = criticalPath ? null : "Traces",
            Language = "KQL",
            TraceId = criticalPath ? Id : null,
            TimeRange = new() { StartTime = "2026-01-02T00:00:00Z", EndTime = "2026-01-02T01:00:00Z" },
            Options = new() { MaxResults = 10000 },
        },
        CompareTo = new()
        {
            Query = criticalPath ? null : "Traces | where error_rate > 0",
            TraceId = criticalPath ? ComparisonId : null,
            TimeRange = new() { StartTime = "2026-01-01T00:00:00Z", EndTime = "2026-01-01T01:00:00Z" },
        },
    };
}

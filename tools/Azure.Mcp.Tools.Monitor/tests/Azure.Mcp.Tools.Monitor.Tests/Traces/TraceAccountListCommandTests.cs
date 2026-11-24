// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Azure.Mcp.Core.Services.Azure;
using Azure.Mcp.Tests.Commands;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Commands.Traces;
using Azure.Mcp.Tools.Monitor.Models.Traces;
using Azure.Mcp.Tools.Monitor.Options.Traces;
using Azure.Mcp.Tools.Monitor.Services;
using Azure.Mcp.Tools.Monitor.Services.Traces;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.Traces;

public sealed class TraceAccountListCommandTests : SubscriptionCommandUnitTestsBase<TraceAccountListCommand, IMonitorTracesService>
{
    private const string KnownSubscription = "knownSubscription";

    [Theory]
    [InlineData("explicit-subscription", "explicit-subscription")]
    [InlineData(null, "default-subscription")]
    public async Task ExecuteAsync_TagsResolvedSubscription(string? subscription, string expected)
    {
        SubscriptionResolver.ResolveSubscription(Arg.Any<string?>()).Returns(call => call.Arg<string?>() ?? "default-subscription");
        Service.ListAccountsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ResourceQueryResults<TraceAccountInfo>([], false));
        var parameters = new Dictionary<string, JsonElement>();
        if (subscription is not null)
        {
            parameters["subscription"] = JsonSerializer.SerializeToElement(subscription);
        }

        Assert.True(CommandDefinition.TryParseFromDictionary(parameters, out var parsed, out _));
        using var activity = new Activity(ActivityName.ToolExecuted).Start();
        var response = await ((IBaseCommand)Command).ExecuteAsync(new CommandContext(activity), parsed!, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(expected, activity.GetTagItem("AzSubscriptionGuid"));
        await Service.Received(1).ListAccountsAsync(expected, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PostBindOptions_DoesNotTagUnrelatedActivity()
    {
        using var activity = new Activity("unrelated").Start();
        Command.PostBindOptions(new TraceAccountListOptions { Subscription = KnownSubscription });

        Assert.Null(activity.GetTagItem("AzSubscriptionGuid"));
    }

    [Theory]
    [InlineData($"--subscription {KnownSubscription}", true)]
    [InlineData($"--subscription {KnownSubscription} --resource-group my-rg", true)]
    [InlineData("", false)]
    public async Task ExecuteAsync_ValidatesInputCorrectly(string args, bool shouldSucceed)
    {
        if (shouldSucceed)
        {
            Service.ListAccountsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new ResourceQueryResults<TraceAccountInfo>([], false));
        }

        var response = await ExecuteCommandAsync(args);

        Assert.Equal(shouldSucceed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ReturnsAccountsAndTruncationStatus(bool areResultsTruncated)
    {
        var accounts = new List<TraceAccountInfo>
        {
            new() { Name = "acct1", ResourceId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Monitor/accounts/acct1", ResourceGroup = "rg", Location = "eastus" },
            new() { Name = "acct2", ResourceId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Monitor/accounts/acct2", ResourceGroup = "rg", Location = "westus", TraceDurationWindowInSeconds = 3600 }
        };
        Service.ListAccountsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ResourceQueryResults<TraceAccountInfo>(accounts, areResultsTruncated));

        var response = await ExecuteCommandAsync($"--subscription {KnownSubscription}");
        var result = ValidateAndDeserializeResponse(response, MonitorJsonContext.Default.TraceAccountListCommandResult);

        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal("acct1", result.Accounts[0].Name);
        Assert.Equal(3600, result.Accounts[1].TraceDurationWindowInSeconds);
        Assert.Equal(areResultsTruncated, result.AreResultsTruncated);
    }

    [Fact]
    public async Task ExecuteAsync_WithResourceGroup_ForwardsToService()
    {
        const string resourceGroup = "trace-rg";
        Service.ListAccountsAsync(Arg.Any<string>(), Arg.Is(resourceGroup), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ResourceQueryResults<TraceAccountInfo>([], false));

        var response = await ExecuteCommandAsync($"--subscription {KnownSubscription} --resource-group {resourceGroup}");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        await Service.Received(1).ListAccountsAsync(Arg.Any<string>(), Arg.Is(resourceGroup), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HandlesServiceErrors()
    {
        Service.ListAccountsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await ExecuteCommandAsync($"--subscription {KnownSubscription}");

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
        Assert.Contains("boom", response.Message);
    }
}

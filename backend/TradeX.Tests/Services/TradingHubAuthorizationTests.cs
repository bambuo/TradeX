using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeX.Api.Hubs;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Tests.Services;

public class TradingHubAuthorizationTests
{
    [Fact]
    public async Task JoinTraderGroup_AllowsOwner()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var repo = Substitute.For<ITraderRepository>();
        repo.GetByIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns(new Trader { Id = traderId, UserId = userId });
        var groups = Substitute.For<IGroupManager>();
        var hub = CreateHub(repo, groups, userId, UserRole.Operator);

        await hub.JoinTraderGroup(traderId.ToString());

        await groups.Received(1).AddToGroupAsync("connection-1", $"trader_{traderId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinTraderGroup_RejectsOtherUsersTrader()
    {
        var traderId = Guid.NewGuid();
        var repo = Substitute.For<ITraderRepository>();
        repo.GetByIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns(new Trader { Id = traderId, UserId = Guid.NewGuid() });
        var groups = Substitute.For<IGroupManager>();
        var hub = CreateHub(repo, groups, Guid.NewGuid(), UserRole.Operator);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinTraderGroup(traderId.ToString()));

        await groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
    }

    [Fact]
    public async Task JoinTraderGroup_AllowsAdmin()
    {
        var traderId = Guid.NewGuid();
        var repo = Substitute.For<ITraderRepository>();
        var groups = Substitute.For<IGroupManager>();
        var hub = CreateHub(repo, groups, Guid.NewGuid(), UserRole.Admin);

        await hub.JoinTraderGroup(traderId.ToString());

        await repo.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await groups.Received(1).AddToGroupAsync("connection-1", $"trader_{traderId}", Arg.Any<CancellationToken>());
    }

    private static TradingHub CreateHub(
        ITraderRepository repo,
        IGroupManager groups,
        Guid userId,
        UserRole role)
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection-1");
        context.ConnectionAborted.Returns(CancellationToken.None);
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        ], "Test")));

        return new TradingHub(repo, NullLogger<TradingHub>.Instance)
        {
            Context = context,
            Groups = groups
        };
    }
}

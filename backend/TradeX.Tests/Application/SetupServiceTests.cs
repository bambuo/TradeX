using NSubstitute;
using TradeX.Application.Common;
using TradeX.Application.Setup;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Tests.Application;

public sealed class SetupServiceTests
{
    // ─────────────── GetStatusAsync ───────────────

    [Fact]
    public async Task GetStatusAsync_WhenNoSuperAdmin_ShouldReturnFalse()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        userRepo.GetAllAsync(default).Returns([]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.GetStatusAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task GetStatusAsync_WhenSuperAdminExists_ShouldReturnTrue()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        var admin = User.Create("admin", "admin@test.com", "hash", UserRole.SuperAdmin);
        userRepo.GetAllAsync(default).Returns([admin]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.GetStatusAsync();

        Assert.True(result);
    }

    // ─────────────── InitializeAsync ───────────────

    [Fact]
    public async Task InitializeAsync_WhenAlreadyInitialized_ShouldReturnConflict()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        var admin = User.Create("admin", "admin@test.com", "hash", UserRole.SuperAdmin);
        userRepo.GetAllAsync(default).Returns([admin]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.InitializeAsync("newadmin", "password123", null);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("系统已初始化", result.Error);
    }

    [Fact]
    public async Task InitializeAsync_WithShortUserName_ShouldReturnBadRequest()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        userRepo.GetAllAsync(default).Returns([]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.InitializeAsync("ab", "password123", null);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("至少 3 个字符", result.Error);
    }

    [Fact]
    public async Task InitializeAsync_WithShortPassword_ShouldReturnBadRequest()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        userRepo.GetAllAsync(default).Returns([]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.InitializeAsync("validuser", "short", null);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("至少 8 个字符", result.Error);
    }

    [Fact]
    public async Task InitializeAsync_ShouldCreateSuperAdminAndDefaultConfig()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        userRepo.GetAllAsync(default).Returns([]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.InitializeAsync("superadmin", "StrongPass1", "custom-jwt-secret");

        Assert.True(result.Success);
        Assert.Equal(204, result.StatusCode);

        // 验证创建了超级管理员
        await userRepo.Received(1).AddAsync(
            Arg.Is<User>(u => u.Username == "superadmin" && u.Role == UserRole.SuperAdmin && u.Status == UserStatus.PendingMfa),
            default);

        // 验证插入了默认配置
        await configRepo.Received(1).UpsertAsync("jwt.secret", "custom-jwt-secret", default);
        await configRepo.Received(1).UpsertAsync("jwt.access_token_expires_minutes", "30", default);
        await configRepo.Received(1).UpsertAsync("jwt.refresh_token_expires_days", "7", default);
        await configRepo.Received(1).UpsertAsync("risk.default_slippage_percent", "0.1", default);
        await configRepo.Received(1).UpsertAsync("risk.max_daily_loss_percent", "10", default);
        await configRepo.Received(1).UpsertAsync("risk.max_drawdown_percent", "25", default);
        await configRepo.Received(1).UpsertAsync("risk.cooldown_seconds", "300", default);
        await configRepo.Received(1).UpsertAsync("risk.volatility_grid_dedup_seconds", "60", default);
        await configRepo.Received(1).UpsertAsync("risk.consecutive_loss_limit", "5", default);
        await configRepo.Received(1).UpsertAsync("data.kline_warmup_days", "3", default);
        await configRepo.Received(1).UpsertAsync("data.kline_warmup_interval", "15m", default);

        // 验证 upsert 被调用了 11 次（jwt.secret + 10 个默认值）
        await configRepo.Received(11).UpsertAsync(Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task InitializeAsync_WithoutJwtSecret_ShouldGenerateRandom()
    {
        var userRepo = Substitute.For<IUserRepository>();
        var configRepo = Substitute.For<ISystemConfigRepository>();
        userRepo.GetAllAsync(default).Returns([]);

        var service = new SetupService(userRepo, configRepo);
        var result = await service.InitializeAsync("superadmin", "StrongPass1", null);

        Assert.True(result.Success);

        // 验证 jwt.secret 不是空的（且是 Base64 字符串）
        await configRepo.Received(1).UpsertAsync("jwt.secret",
            Arg.Is<string>(s => !string.IsNullOrWhiteSpace(s)), default);
    }
}

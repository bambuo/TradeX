using Microsoft.EntityFrameworkCore;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data.Entities;

namespace TradeX.Infrastructure.Data;

public class TradeXDbContext(DbContextOptions<TradeXDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MfaSecret> MfaSecrets => Set<MfaSecret>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<Trader> Traders => Set<Trader>();
    public DbSet<Exchange> Exchanges => Set<Exchange>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<StrategyBinding> StrategyBindings => Set<StrategyBinding>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<BacktestTask> BacktestTasks => Set<BacktestTask>();
    public DbSet<BacktestResult> BacktestResults => Set<BacktestResult>();
    public DbSet<BacktestKlineAnalysisEntity> BacktestKlineAnalyses => Set<BacktestKlineAnalysisEntity>();
    public DbSet<Pair> Pairs => Set<Pair>();
    public DbSet<ExchangePairRuleSnapshot> ExchangePairRules => Set<ExchangePairRuleSnapshot>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradeXDbContext).Assembly);
    }
}

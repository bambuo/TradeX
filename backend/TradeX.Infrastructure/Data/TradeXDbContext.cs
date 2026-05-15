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
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MfaSecretEncrypted).HasMaxLength(512);
            e.Property(x => x.RecoveryCodesJson).HasMaxLength(2000);
        });

        modelBuilder.Entity<MfaSecret>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.SecretKey).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Token).HasMaxLength(512).IsRequired();
        });

        modelBuilder.Entity<RecoveryCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Code });
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<SystemConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<Trader>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AvatarColor).HasMaxLength(20);
            e.Property(x => x.AvatarUrl).HasMaxLength(500);
            e.Property(x => x.Style).HasMaxLength(20);
        });

        modelBuilder.Entity<Exchange>(e =>
        {
            e.ToTable("Exchanges");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ApiKeyEncrypted).IsRequired();
            e.Property(x => x.SecretKeyEncrypted).IsRequired();
            e.Property(x => x.PassphraseEncrypted).HasMaxLength(512);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TestResult).HasMaxLength(500);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.Resource).HasMaxLength(100).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<Strategy>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<StrategyBinding>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Timeframe).HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Pairs).HasMaxLength(500);
        });

        modelBuilder.Entity<Position>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TraderId, x.Status });
            e.HasIndex(x => new { x.ExchangeId, x.Pair, x.Status });
            e.Property(x => x.Pair).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Quantity).HasColumnType("decimal(28,12)");
            e.Property(x => x.EntryPrice).HasColumnType("decimal(28,12)");
            e.Property(x => x.CurrentPrice).HasColumnType("decimal(28,12)");
            e.Property(x => x.UnrealizedPnl).HasColumnType("decimal(28,12)");
            e.Property(x => x.RealizedPnl).HasColumnType("decimal(28,12)");
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<BacktestTask>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StrategyId);
            e.HasIndex(x => x.ExchangeId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.StrategyName).HasMaxLength(200);
            e.Property(x => x.Pair).HasMaxLength(50);
            e.Property(x => x.Timeframe).HasMaxLength(10);
        });

        modelBuilder.Entity<BacktestResult>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TaskId).IsUnique();
        });

        modelBuilder.Entity<BacktestKlineAnalysisEntity>(e =>
        {
            e.ToTable("BacktestKlineAnalyses");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TaskId);
            e.HasIndex(x => new { x.TaskId, x.Index }).IsUnique();
            e.Property(x => x.Action).HasMaxLength(20);
            e.Property(x => x.IndicatorsJson).HasMaxLength(4000);
        });

        modelBuilder.Entity<Pair>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ExchangeId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(30).IsRequired();
            e.Property(x => x.BaseAsset).HasMaxLength(20).IsRequired();
            e.Property(x => x.QuoteAsset).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<ExchangePairRuleSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExchangeId);
            e.Property(x => x.Pair).HasMaxLength(30).IsRequired();
        });

        modelBuilder.Entity<NotificationChannel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ConfigEncrypted).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TraderId);
            e.HasIndex(x => x.ClientOrderId).IsUnique();
            e.HasIndex(x => x.ExchangeOrderId).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Pair).HasMaxLength(50).IsRequired();
            e.Property(x => x.ExchangeOrderId).HasMaxLength(100);
            e.Property(x => x.FeeAsset).HasMaxLength(20);
            e.Property(x => x.Side).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Price).HasColumnType("decimal(28,12)");
            e.Property(x => x.Quantity).HasColumnType("decimal(28,12)");
            e.Property(x => x.FilledQuantity).HasColumnType("decimal(28,12)");
            e.Property(x => x.QuoteQuantity).HasColumnType("decimal(28,12)");
            e.Property(x => x.Fee).HasColumnType("decimal(28,12)");
            e.Property(x => x.Version).IsConcurrencyToken();
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class BacktestTaskConfiguration : IEntityTypeConfiguration<BacktestTask>
{
    public void Configure(EntityTypeBuilder<BacktestTask> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.StrategyId);
        builder.HasIndex(x => x.ExchangeId);
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.StrategyName).HasMaxLength(200);
        builder.Property(x => x.Pair).HasMaxLength(50);
        builder.Property(x => x.Timeframe).HasMaxLength(10);
    }
}

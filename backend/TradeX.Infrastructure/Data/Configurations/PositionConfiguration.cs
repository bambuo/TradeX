using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TraderId, x.Status });
        builder.HasIndex(x => new { x.ExchangeId, x.Pair, x.Status });
        builder.HasIndex(x => new { x.StrategyId, x.Pair, x.Status });
        // 开仓订单唯一约束：保证"成交→持仓"投影幂等（同一买单只开一条持仓）。
        // 过滤索引仅约束非 NULL 行，避免历史/手工持仓（OpeningOrderId 为空）相互冲突。
        builder.HasIndex(x => x.OpeningOrderId)
            .IsUnique()
            .HasFilter("\"OpeningOrderId\" IS NOT NULL");
        builder.Property(x => x.Pair).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Quantity).HasColumnType("decimal(28,12)");
        builder.Property(x => x.EntryPrice).HasColumnType("decimal(28,12)");
        builder.Property(x => x.CurrentPrice).HasColumnType("decimal(28,12)");
        builder.Property(x => x.UnrealizedPnl).HasColumnType("decimal(28,12)");
        builder.Property(x => x.RealizedPnl).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}

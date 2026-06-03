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

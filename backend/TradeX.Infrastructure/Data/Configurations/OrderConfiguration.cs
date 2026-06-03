using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TraderId);
        builder.HasIndex(x => x.ClientOrderId).IsUnique();
        builder.HasIndex(x => x.ExchangeOrderId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.Pair).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExchangeOrderId).HasMaxLength(100);
        builder.Property(x => x.FeeAsset).HasMaxLength(20);
        builder.Property(x => x.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Price).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Quantity).HasColumnType("decimal(28,12)");
        builder.Property(x => x.FilledQuantity).HasColumnType("decimal(28,12)");
        builder.Property(x => x.QuoteQuantity).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Fee).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}

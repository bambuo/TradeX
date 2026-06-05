using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class ExchangeOrderHistoryConfiguration : IEntityTypeConfiguration<ExchangeOrderHistory>
{
    public void Configure(EntityTypeBuilder<ExchangeOrderHistory> builder)
    {
        builder.ToTable("ExchangeOrderHistories");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ExchangeId, x.ExchangeOrderId })
            .IsUnique()
            .HasDatabaseName("IX_ExchangeOrderHistories_ExchangeId_OrderId");

        builder.HasIndex(x => new { x.ExchangeId, x.PlacedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ExchangeOrderHistories_ExchangeId_PlacedAt");

        builder.Property(x => x.Pair).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ExchangeOrderId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Price).HasColumnType("decimal(28,12)");
        builder.Property(x => x.Quantity).HasColumnType("decimal(28,12)");
        builder.Property(x => x.FilledQuantity).HasColumnType("decimal(28,12)");
    }
}

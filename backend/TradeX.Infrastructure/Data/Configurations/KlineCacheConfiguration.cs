using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Infrastructure.Data.Entities;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class KlineCacheConfiguration : IEntityTypeConfiguration<KlineCacheEntity>
{
    public void Configure(EntityTypeBuilder<KlineCacheEntity> builder)
    {
        builder.ToTable("KlineCache");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ExchangeId, x.Pair, x.Timeframe, x.Timestamp }).IsUnique();
        builder.Property(x => x.Pair).HasMaxLength(32);
        builder.Property(x => x.Timeframe).HasMaxLength(10);
    }
}

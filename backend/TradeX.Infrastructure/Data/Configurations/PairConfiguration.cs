using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class PairConfiguration : IEntityTypeConfiguration<Pair>
{
    public void Configure(EntityTypeBuilder<Pair> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ExchangeId, x.Name }).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(30).IsRequired();
        builder.Property(x => x.BaseAsset).HasMaxLength(20).IsRequired();
        builder.Property(x => x.QuoteAsset).HasMaxLength(20).IsRequired();
    }
}

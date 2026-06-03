using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class ExchangePairRuleSnapshotConfiguration : IEntityTypeConfiguration<ExchangePairRuleSnapshot>
{
    public void Configure(EntityTypeBuilder<ExchangePairRuleSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ExchangeId);
        builder.Property(x => x.Pair).HasMaxLength(30).IsRequired();
    }
}

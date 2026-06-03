using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class StrategyBindingConfiguration : IEntityTypeConfiguration<StrategyBinding>
{
    public void Configure(EntityTypeBuilder<StrategyBinding> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.Timeframe).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Pairs).HasMaxLength(500);
    }
}

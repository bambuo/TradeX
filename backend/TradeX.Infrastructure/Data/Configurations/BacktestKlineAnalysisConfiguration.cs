using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Infrastructure.Data.Entities;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class BacktestKlineAnalysisConfiguration : IEntityTypeConfiguration<BacktestKlineAnalysisEntity>
{
    public void Configure(EntityTypeBuilder<BacktestKlineAnalysisEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TaskId);
        builder.HasIndex(x => new { x.TaskId, x.Index }).IsUnique();
        builder.Property(x => x.Action).HasMaxLength(20);
        builder.Property(x => x.IndicatorValues)
            .HasMaxLength(4000);
    }
}

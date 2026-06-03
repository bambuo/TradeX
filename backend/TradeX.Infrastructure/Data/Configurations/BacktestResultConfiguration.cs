using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class BacktestResultConfiguration : IEntityTypeConfiguration<BacktestResult>
{
    public void Configure(EntityTypeBuilder<BacktestResult> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TaskId).IsUnique();
    }
}

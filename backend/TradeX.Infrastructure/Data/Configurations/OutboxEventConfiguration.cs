using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Type).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.LastError).HasMaxLength(2000);
    }
}

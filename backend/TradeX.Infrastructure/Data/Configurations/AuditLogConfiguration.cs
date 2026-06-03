using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Resource).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(50).IsRequired();
    }
}

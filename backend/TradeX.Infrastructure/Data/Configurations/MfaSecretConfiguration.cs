using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Configurations;

public sealed class MfaSecretConfiguration : IEntityTypeConfiguration<MfaSecret>
{
    public void Configure(EntityTypeBuilder<MfaSecret> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.SecretKey).IsRequired();
    }
}

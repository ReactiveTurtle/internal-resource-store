using InternalResourceStore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternalResourceStore.Infrastructure.Persistence.Configurations;

public sealed class SystemVariableConfiguration : IEntityTypeConfiguration<SystemVariable>
{
    public void Configure(EntityTypeBuilder<SystemVariable> builder)
    {
        builder.ToTable("system_variables");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}

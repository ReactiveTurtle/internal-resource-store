using InternalResourceStore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternalResourceStore.Infrastructure.Persistence.Configurations;

public sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("resources");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.OwnerApiKeyHash).HasColumnName("owner_api_key_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.ImageWidth).HasColumnName("image_width").IsRequired();
        builder.Property(x => x.ImageHeight).HasColumnName("image_height").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.PurgedAt).HasColumnName("purged_at");
        builder.HasIndex(x => x.OwnerApiKeyHash);
        builder.HasIndex(x => new { x.DeletedAt, x.PurgedAt });
    }
}

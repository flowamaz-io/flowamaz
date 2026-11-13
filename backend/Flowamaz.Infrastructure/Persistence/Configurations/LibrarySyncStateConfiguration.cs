using Flowamaz.Core.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class LibrarySyncStateConfiguration : IEntityTypeConfiguration<LibrarySyncState>
{
    public void Configure(EntityTypeBuilder<LibrarySyncState> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Source).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Repository).HasMaxLength(256).IsRequired();
        builder.Property(s => s.LastCommitSha).HasMaxLength(64);
        builder.Property(s => s.LastError).HasMaxLength(2000);
        builder.HasIndex(s => s.Source).IsUnique();
    }
}

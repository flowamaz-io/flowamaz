using Flowamaz.Core.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class PublisherProfileConfiguration : IEntityTypeConfiguration<PublisherProfile>
{
    private static readonly DateTime SeedTimestamp = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PublisherProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PublisherId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Bio).HasMaxLength(600).IsRequired();
        builder.Property(p => p.WebsiteUrl).HasMaxLength(200);
        builder.Property(p => p.GithubLogin).HasMaxLength(39);
        builder.HasIndex(p => p.PublisherId).IsUnique();
        builder.HasIndex(p => p.OrgId);
        builder.HasData(new PublisherProfile
        {
            Id = Guid.Parse("00000000-0000-0000-0007-000000000001"),
            PublisherId = "flowamaz-io",
            DisplayName = "Flowamaz",
            Bio = "Official connectors, templates, forms and packs, built and maintained by the Flowamaz team.",
            WebsiteUrl = "https://flowamaz.com",
            GithubLogin = "flowamaz-io",
            IsVerified = true,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        });
    }
}

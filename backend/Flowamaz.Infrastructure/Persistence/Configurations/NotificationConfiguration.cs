using Flowamaz.Core.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Type).HasMaxLength(32).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(256).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1024).IsRequired();
        builder.Property(n => n.ActionUrl).HasMaxLength(1024);
        // Hot path: unread notifications for a user, newest first.
        builder.HasIndex(n => new { n.UserId, n.IsRead });
    }
}

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Value).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(p => new { p.UserId, p.Key }).IsUnique();
    }
}

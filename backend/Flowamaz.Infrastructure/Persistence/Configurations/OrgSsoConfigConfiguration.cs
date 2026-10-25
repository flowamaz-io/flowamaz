using Flowamaz.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class OrgSsoConfigConfiguration : IEntityTypeConfiguration<OrgSsoConfig>
{
    public void Configure(EntityTypeBuilder<OrgSsoConfig> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Provider).HasMaxLength(16).IsRequired();
        builder.HasIndex(c => c.OrgId).IsUnique();

        builder.Property(c => c.Scopes)
            .HasConversion(JsonColumn.Converter<List<string>>(), JsonColumn.Comparer<List<string>>())
            .HasColumnType("jsonb");

        builder.Property(c => c.DefaultRole).HasConversion<string>().HasMaxLength(16).HasDefaultValue(Core.Enums.WorkspaceRole.Viewer);
        builder.Property(c => c.JitProvisioning).HasDefaultValue(true);
        builder.Property(c => c.GroupsAttribute).HasMaxLength(256).HasDefaultValue("groups");
        builder.Property(c => c.GroupRoleMappings)
            .HasConversion(JsonColumn.Converter<List<Core.Interfaces.Services.SsoGroupRoleMapping>>(),
                JsonColumn.Comparer<List<Core.Interfaces.Services.SsoGroupRoleMapping>>())
            .HasColumnType("jsonb");
    }
}

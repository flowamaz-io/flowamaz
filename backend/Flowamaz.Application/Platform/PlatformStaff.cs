using Microsoft.Extensions.Configuration;

namespace Flowamaz.Application.Platform;

/// <summary>
/// Who counts as Flowamaz staff for platform screens (such as setting an Enterprise customer's agreed API limit):
/// anyone signed in to an organisation listed in <c>Platform:StaffOrgIds</c> (comma-separated, or a list; env var
/// <c>Platform__StaffOrgIds</c>). Organisation ids are random and come from the signed access token, so nobody can
/// make their own organisation count. With nothing configured there are no staff, and the screens stay closed.
/// </summary>
public static class PlatformStaff
{
    public const string ConfigKey = "Platform:StaffOrgIds";

    public static bool IsStaffOrg(IConfiguration configuration, Guid orgId)
    {
        if (orgId == Guid.Empty) return false;
        return StaffOrgIds(configuration).Contains(orgId);
    }

    public static HashSet<Guid> StaffOrgIds(IConfiguration configuration)
    {
        var ids = new HashSet<Guid>();
        var section = configuration.GetSection(ConfigKey);
        var values = section.GetChildren().Select(c => c.Value).Append(section.Value);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (var part in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                if (Guid.TryParse(part, out var id)) ids.Add(id);
        }
        return ids;
    }
}

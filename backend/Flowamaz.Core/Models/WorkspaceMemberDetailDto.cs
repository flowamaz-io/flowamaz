using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>Member read model enriched with the org user's email and name (for the members list).</summary>
public sealed record WorkspaceMemberDetailDto(
    Guid OrgUserId,
    string Email,
    string Name,
    WorkspaceRole Role,
    DateTime JoinedAt,
    bool IsActive);

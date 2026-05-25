using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Models;

/// <summary>Read model for a workspace member. No credential or secret data.</summary>
public sealed record WorkspaceMemberDto(
    Guid WorkspaceId,
    Guid OrgUserId,
    WorkspaceRole Role,
    DateTime JoinedAt,
    bool IsActive);

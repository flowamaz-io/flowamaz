namespace Flowamaz.Application.Auth.DTOs;

/// <summary>Minimal user identity returned alongside an access token.</summary>
public sealed record UserSummary(Guid Id, string Email, string Name, Guid OrgId, string OrgSlug);

/// <summary>A workspace the current user belongs to, with their role (for the /me view).</summary>
public sealed record WorkspaceSummary(Guid Id, string Slug, string Name, string Role);

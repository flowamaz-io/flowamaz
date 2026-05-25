namespace Flowamaz.Application.Auth.DTOs;

/// <summary>Login credentials. Org is identified by slug; email is unique within that org.</summary>
public sealed record LoginRequest(string Email, string Password, string OrgSlug);

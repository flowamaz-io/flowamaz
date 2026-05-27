namespace Flowamaz.Application.Auth.DTOs;

public sealed record ResetPasswordRequest(string Token, string OrgSlug, string NewPassword);

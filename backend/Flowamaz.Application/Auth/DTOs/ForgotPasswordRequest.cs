namespace Flowamaz.Application.Auth.DTOs;

public sealed record ForgotPasswordRequest(string Email, string OrgSlug);

namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Cuts off access tokens that were issued before a point in time. Access tokens are short-lived (about
/// 15 minutes) and not stored, so ending a refresh token alone leaves the current one working until it
/// expires. A revocation marker closes that gap: the auth middleware rejects any access token issued
/// before the marker. Markers live only as long as an access token can, then expire on their own.
/// <para>
/// When the refresh tokens are still valid (for example after a workspace role change), the browser simply
/// refreshes and gets a new token with the current roles; when they were revoked too, the person is signed out.
/// </para>
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>Rejects every access token this user holds now.</summary>
    Task RevokeUserAccessAsync(Guid orgUserId, CancellationToken ct = default);

    /// <summary>Rejects every access token these people hold now (one round trip).</summary>
    Task RevokeUsersAccessAsync(IReadOnlyCollection<Guid> orgUserIds, CancellationToken ct = default);

    /// <summary>Rejects every access token held now by members of the organisation who are not owners.</summary>
    Task RevokeOrgNonOwnerAccessAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>
    /// True when a token issued at <paramref name="issuedAtUnixMs"/> was revoked for this user (or, for a
    /// non-owner, for their organisation).
    /// </summary>
    Task<bool> IsRevokedAsync(Guid orgUserId, Guid orgId, bool isOrgOwner, long issuedAtUnixMs, CancellationToken ct = default);
}

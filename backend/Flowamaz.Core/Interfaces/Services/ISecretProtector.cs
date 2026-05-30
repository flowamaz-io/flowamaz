namespace Flowamaz.Core.Interfaces.Services;

/// <summary>
/// Symmetric protection for short secrets (webhook HMAC keys, etc.) that — unlike vault
/// credentials — must be decryptable for verification on every request. Uses AES-256-GCM
/// with a per-workspace key derived from CREDENTIAL_MASTER_KEY. The protected form is a
/// self-contained base64 string (nonce||ciphertext||tag); secrets are never logged.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> for <paramref name="workspaceId"/>.</summary>
    string Protect(Guid workspaceId, string plaintext);

    /// <summary>Decrypts a value previously produced by <see cref="Protect"/> for the same workspace.</summary>
    string Unprotect(Guid workspaceId, string protectedValue);
}

using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// AES-256-GCM protector for short, re-readable secrets (webhook HMAC keys, IdP client secrets).
///
/// Key derivation matches the credential vault: HMAC-SHA256(CREDENTIAL_MASTER_KEY, workspaceId)
/// → 32-byte per-workspace key. Each value is encrypted with a fresh random nonce; the protected
/// form is base64(nonce || ciphertext || tag), so decryption needs only the workspace id.
/// Plain secrets are never logged.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IConfiguration _configuration;

    public AesGcmSecretProtector(IConfiguration configuration) => _configuration = configuration;

    public string Protect(Guid workspaceId, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = DeriveWorkspaceKey(workspaceId);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        var combined = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(combined, 0);
        ciphertext.CopyTo(combined, NonceSize);
        tag.CopyTo(combined, NonceSize + ciphertext.Length);
        return Convert.ToBase64String(combined);
    }

    public string Unprotect(Guid workspaceId, string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        var key = DeriveWorkspaceKey(workspaceId);
        var combined = Convert.FromBase64String(protectedValue);

        var nonce = combined[..NonceSize];
        var tag = combined[^TagSize..];
        var ciphertext = combined[NonceSize..^TagSize];
        var plain = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] DeriveWorkspaceKey(Guid workspaceId)
    {
        var masterKeyStr = _configuration["CREDENTIAL_MASTER_KEY"]
            ?? throw new InvalidOperationException(
                "CREDENTIAL_MASTER_KEY environment variable is not set. " +
                "Set this variable before starting the application. " +
                "Use a securely generated 32+ character random string.");

        var masterKeyBytes = Encoding.UTF8.GetBytes(masterKeyStr);
        var workspaceBytes = workspaceId.ToByteArray();
        return HMACSHA256.HashData(masterKeyBytes, workspaceBytes);
    }
}

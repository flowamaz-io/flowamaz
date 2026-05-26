using System.Security.Cryptography;
using System.Text;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Infrastructure.Services;

/// <summary>
/// AES-256-GCM credential vault.
///
/// Key derivation: HMAC-SHA256(CREDENTIAL_MASTER_KEY, workspaceId bytes) → 32-byte workspace
/// master key. A fresh random 32-byte DEK is generated per credential, encrypted with the
/// workspace master key (AES-256-GCM), then the DEK encrypts the plaintext value (AES-256-GCM).
///
/// Layout stored per credential:
///   EncryptedKey = AES-GCM(workspaceMK, dek)  — 12 byte nonce + ciphertext + 16 byte tag
///   IV           = nonce for the VALUE cipher
///   EncryptedValue = AES-GCM(dek, plaintext)
///   Tag          = authentication tag for the VALUE cipher
///
/// Plain values are NEVER logged. Workspace isolation is enforced on every retrieve/revoke.
/// </summary>
public sealed class CredentialVaultService : ICredentialVaultService
{
    private const int NonceSize = 12;   // AES-GCM standard nonce
    private const int TagSize = 16;     // AES-GCM standard tag
    private const int KeySize = 32;     // AES-256

    private readonly FlowAmazDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CredentialVaultService> _logger;

    public CredentialVaultService(
        FlowAmazDbContext db,
        IConfiguration configuration,
        ILogger<CredentialVaultService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Guid> StoreAsync(
        Guid workspaceId,
        string connectorId,
        string name,
        string authType,
        string plainValue,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "CredentialVaultService.StoreAsync enter workspace={WorkspaceId} connectorId={ConnectorId} name={Name}",
            workspaceId, connectorId, name);

        try
        {
            var connectorDef = await _db.ConnectorDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConnectorId == connectorId && !c.IsDeleted, ct)
                ?? throw new InvalidOperationException(
                    $"Connector '{connectorId}' not found. Ensure the connector is installed before storing credentials.");

            var workspaceMk = DeriveWorkspaceMasterKey(workspaceId);

            // Generate fresh DEK
            var dek = RandomNumberGenerator.GetBytes(KeySize);

            // Encrypt DEK with workspace master key
            var (encryptedKey, keyNonce, keyTag) = AesGcmEncrypt(workspaceMk, dek);

            // Encrypt plain value with DEK
            var plainBytes = Encoding.UTF8.GetBytes(plainValue);
            var (encryptedValue, valueNonce, valueTag) = AesGcmEncrypt(dek, plainBytes);

            // Combine keyNonce + keyTag into EncryptedKey storage (nonce||ciphertext||tag)
            var encryptedKeyFull = CombineNonceCiphertextTag(keyNonce, encryptedKey, keyTag);

            var credential = new WorkspaceCredential
            {
                WorkspaceId = workspaceId,
                ConnectorDefinitionId = connectorDef.Id,
                Name = name,
                AuthType = authType,
                EncryptedValue = encryptedValue,
                EncryptedKey = encryptedKeyFull,
                IV = valueNonce,
                Tag = valueTag,
                IsActive = true
            };

            await _db.WorkspaceCredentials.AddAsync(credential, ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "CredentialVaultService.StoreAsync exit credentialId={CredentialId} workspace={WorkspaceId}",
                credential.Id, workspaceId);

            return credential.Id;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "CredentialVaultService.StoreAsync error workspace={WorkspaceId} connectorId={ConnectorId}",
                workspaceId, connectorId);
            throw;
        }
    }

    public async Task<string> RetrieveAsync(Guid workspaceId, Guid credentialId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "CredentialVaultService.RetrieveAsync enter workspace={WorkspaceId} credentialId={CredentialId}",
            workspaceId, credentialId);

        try
        {
            var credential = await _db.WorkspaceCredentials
                .FirstOrDefaultAsync(c => c.Id == credentialId && c.WorkspaceId == workspaceId && !c.IsDeleted, ct)
                ?? throw new UnauthorizedAccessException(
                    $"Credential '{credentialId}' not found or access denied. Cross-workspace access is not permitted.");

            var workspaceMk = DeriveWorkspaceMasterKey(workspaceId);

            // Unwrap DEK from EncryptedKey (nonce||ciphertext||tag)
            SplitNonceCiphertextTag(credential.EncryptedKey, KeySize, out var keyNonce, out var encryptedDek, out var keyTag);
            var dek = AesGcmDecrypt(workspaceMk, keyNonce, encryptedDek, keyTag);

            // Decrypt value
            var plainBytes = AesGcmDecrypt(dek, credential.IV, credential.EncryptedValue, credential.Tag);

            // Update last used — fire-and-forget style to avoid slowing the happy path
            credential.LastUsedAt = DateTime.UtcNow;
            _db.WorkspaceCredentials.Update(credential);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "CredentialVaultService.RetrieveAsync exit credentialId={CredentialId} workspace={WorkspaceId}",
                credentialId, workspaceId);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "CredentialVaultService.RetrieveAsync denied credentialId={CredentialId} requestedWorkspace={WorkspaceId}",
                credentialId, workspaceId);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "CredentialVaultService.RetrieveAsync error workspace={WorkspaceId} credentialId={CredentialId}",
                workspaceId, credentialId);
            throw;
        }
    }

    public async Task RevokeAsync(Guid workspaceId, Guid credentialId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "CredentialVaultService.RevokeAsync enter workspace={WorkspaceId} credentialId={CredentialId}",
            workspaceId, credentialId);

        try
        {
            var credential = await _db.WorkspaceCredentials
                .FirstOrDefaultAsync(c => c.Id == credentialId && c.WorkspaceId == workspaceId && !c.IsDeleted, ct)
                ?? throw new UnauthorizedAccessException(
                    $"Credential '{credentialId}' not found or access denied. It may have been revoked or does not belong to this workspace.");

            credential.IsActive = false;
            _db.WorkspaceCredentials.Update(credential);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "CredentialVaultService.RevokeAsync exit credentialId={CredentialId} workspace={WorkspaceId}",
                credentialId, workspaceId);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "CredentialVaultService.RevokeAsync denied credentialId={CredentialId} requestedWorkspace={WorkspaceId}",
                credentialId, workspaceId);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex,
                "CredentialVaultService.RevokeAsync error workspace={WorkspaceId} credentialId={CredentialId}",
                workspaceId, credentialId);
            throw;
        }
    }

    public async Task<IReadOnlyList<CredentialAlias>> ListAsync(Guid workspaceId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "CredentialVaultService.ListAsync enter workspace={WorkspaceId}", workspaceId);

        try
        {
            var aliases = await _db.WorkspaceCredentials
                .AsNoTracking()
                .Where(c => c.WorkspaceId == workspaceId && !c.IsDeleted)
                .Select(c => new CredentialAlias(c.Id, c.Name, c.AuthType, c.ExpiresAt, c.IsActive))
                .ToListAsync(ct);

            _logger.LogInformation(
                "CredentialVaultService.ListAsync exit workspace={WorkspaceId} count={Count}",
                workspaceId, aliases.Count);

            return aliases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CredentialVaultService.ListAsync error workspace={WorkspaceId}", workspaceId);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Crypto helpers
    // -------------------------------------------------------------------------

    private byte[] DeriveWorkspaceMasterKey(Guid workspaceId)
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

    private static (byte[] ciphertext, byte[] nonce, byte[] tag) AesGcmEncrypt(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    private static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static byte[] CombineNonceCiphertextTag(byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, nonce.Length);
        tag.CopyTo(result, nonce.Length + ciphertext.Length);
        return result;
    }

    private static void SplitNonceCiphertextTag(
        byte[] combined,
        int ciphertextLength,
        out byte[] nonce,
        out byte[] ciphertext,
        out byte[] tag)
    {
        nonce = combined[..NonceSize];
        ciphertext = combined[NonceSize..(NonceSize + ciphertextLength)];
        tag = combined[(NonceSize + ciphertextLength)..];
    }
}

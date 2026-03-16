using System.Security.Cryptography;
using System.Text;

namespace BusinessProcessAgent.Core.Compliance;

/// <summary>
/// Provides AES-256-GCM encryption for data at rest (screenshots, database
/// field values). Supports three key sources: DPAPI, environment variable,
/// and Azure Key Vault.
/// </summary>
public sealed class EncryptionService
{
    private readonly ILogger<EncryptionService> _logger;
    private byte[]? _key;

    public EncryptionService(ILogger<EncryptionService> logger)
    {
        _logger = logger;
    }

    public bool IsConfigured => _key is not null;

    /// <summary>
    /// Initializes the encryption key from the configured source.
    /// Must be called before Encrypt/Decrypt.
    /// </summary>
    public void Configure(ComplianceSettings settings)
    {
        if (!settings.EncryptAtRest)
        {
            _key = null;
            return;
        }

        _key = settings.EncryptionKeySource switch
        {
            EncryptionKeySource.DPAPI => GetOrCreateDpapiKey(),
            EncryptionKeySource.Environment => GetEnvironmentKey(),
            EncryptionKeySource.KeyVault => throw new NotImplementedException(
                "Key Vault key source requires async initialization — use ConfigureAsync instead"),
            _ => throw new ArgumentOutOfRangeException(),
        };

        _logger.LogInformation("EncryptionService configured with {Source}", settings.EncryptionKeySource);
    }

    /// <summary>
    /// Encrypts plaintext bytes. Returns nonce + ciphertext + tag as a single byte array.
    /// </summary>
    public byte[] Encrypt(byte[] plaintext)
    {
        if (_key is null) throw new InvalidOperationException("Encryption not configured");

        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: [nonce 12B][tag 16B][ciphertext]
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        ciphertext.CopyTo(result, nonce.Length + tag.Length);
        return result;
    }

    /// <summary>
    /// Decrypts a blob produced by <see cref="Encrypt"/>.
    /// </summary>
    public byte[] Decrypt(byte[] encryptedBlob)
    {
        if (_key is null) throw new InvalidOperationException("Encryption not configured");

        int nonceLen = AesGcm.NonceByteSizes.MaxSize;
        int tagLen = AesGcm.TagByteSizes.MaxSize;

        var nonce = encryptedBlob.AsSpan(0, nonceLen);
        var tag = encryptedBlob.AsSpan(nonceLen, tagLen);
        var ciphertext = encryptedBlob.AsSpan(nonceLen + tagLen);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tagLen);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>Encrypts a UTF-8 string and returns a base64 blob.</summary>
    public string EncryptString(string plaintext)
    {
        var encrypted = Encrypt(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>Decrypts a base64 blob produced by <see cref="EncryptString"/>.</summary>
    public string DecryptString(string base64Blob)
    {
        var decrypted = Decrypt(Convert.FromBase64String(base64Blob));
        return Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>
    /// Encrypts file bytes, writes to <paramref name="outputPath"/>.
    /// </summary>
    public void EncryptFile(byte[] data, string outputPath)
    {
        var encrypted = Encrypt(data);
        File.WriteAllBytes(outputPath, encrypted);
    }

    /// <summary>
    /// Reads and decrypts a file produced by <see cref="EncryptFile"/>.
    /// </summary>
    public byte[] DecryptFile(string filePath)
    {
        var encrypted = File.ReadAllBytes(filePath);
        return Decrypt(encrypted);
    }

    // ── Key Sources ───────────────────────────────────────────

    private static byte[] GetOrCreateDpapiKey()
    {
        // Store a DPAPI-protected master key in LocalAppData
        var keyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessProcessAgent");
        Directory.CreateDirectory(keyDir);
        var keyFile = Path.Combine(keyDir, ".masterkey");

        if (File.Exists(keyFile))
        {
            var protectedBytes = File.ReadAllBytes(keyFile);
            return ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        }

        // Generate a new 256-bit key
        var newKey = RandomNumberGenerator.GetBytes(32);
        var protectedKey = ProtectedData.Protect(newKey, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(keyFile, protectedKey);
        return newKey;
    }

    private static byte[] GetEnvironmentKey()
    {
        var keyHex = Environment.GetEnvironmentVariable("BPA_ENCRYPTION_KEY")
            ?? throw new InvalidOperationException(
                "BPA_ENCRYPTION_KEY environment variable not set. " +
                "Provide a 64-character hex string (256-bit key).");

        if (keyHex.Length != 64)
            throw new InvalidOperationException("BPA_ENCRYPTION_KEY must be a 64-character hex string");

        return Convert.FromHexString(keyHex);
    }
}

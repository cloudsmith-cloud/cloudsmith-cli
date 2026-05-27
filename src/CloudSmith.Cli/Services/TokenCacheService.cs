using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudSmith.Cli.Services;

public sealed class CachedToken
{
    [JsonPropertyName("upn")]
    public string? Upn { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// Refresh token — only populated when the platform keyring stores it
    /// securely. Never written to the filesystem fallback path.
    /// </summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresOn")]
    public DateTimeOffset ExpiresOn { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}

public interface ITokenCacheService
{
    CachedToken? Load();
    void Save(CachedToken token);
    void Clear();
    string TokenFilePath { get; }
}

/// <summary>
/// Persists the MSAL token cache using the OS secure store where available.
///
/// Windows  — DPAPI ProtectedData (CurrentUser scope).
/// Linux    — AES-256-GCM, key derived from /etc/machine-id + username via
///            HMAC-SHA256. The refresh token is NOT written; only the
///            short-lived access token is cached. A one-time warning is
///            emitted so the operator knows re-authentication will be
///            required after expiry.
/// macOS    — Same encrypted-file fallback as Linux (Keychain integration
///            requires a native host app bundle; the dotnet tool package
///            cannot satisfy that constraint). Same refresh-token omission
///            and one-time warning apply.
///
/// There is NO plaintext write path. Any code path that previously returned
/// the raw bytes without encryption has been removed.
/// </summary>
public sealed class TokenCacheService : ITokenCacheService
{
    // AES-GCM parameters (standard sizes mandated by NIST SP 800-38D)
    private const int AesKeyBytes  = 32; // 256-bit key
    private const int AesNonceSize = 12; // 96-bit nonce — required by AesGcm
    private const int AesTagSize   = 16; // 128-bit authentication tag

    // Warning sentinel — written alongside the cache file so we emit the
    // "secure keyring unavailable" warning only once per installation.
    private static readonly string _warningSentinelSuffix = ".nokeyring";

    public string TokenFilePath { get; }

    public TokenCacheService() : this(GetDefaultTokenPath()) { }

    public TokenCacheService(string tokenFilePath)
    {
        TokenFilePath = tokenFilePath;
    }

    public static string GetDefaultTokenPath()
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cloudsmith");
        return Path.Combine(baseDir, "tokens.bin");
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public CachedToken? Load()
    {
        if (!File.Exists(TokenFilePath))
            return null;

        try
        {
            byte[] fileBytes = File.ReadAllBytes(TokenFilePath);
            byte[] jsonBytes = Decrypt(fileBytes);
            string json = Encoding.UTF8.GetString(jsonBytes);
            return JsonSerializer.Deserialize<CachedToken>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(CachedToken token)
    {
        string dir = Path.GetDirectoryName(TokenFilePath)!;
        Directory.CreateDirectory(dir);

        // On non-Windows platforms the file fallback does not store the
        // refresh token — it is short-lived risk only.
        CachedToken toWrite = token;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            toWrite = new CachedToken
            {
                Upn         = token.Upn,
                AccessToken = token.AccessToken,
                RefreshToken = null,   // never persisted in file fallback
                ExpiresOn   = token.ExpiresOn,
                Server      = token.Server,
            };

            EmitKeyringWarningOnce(dir);
        }

        string json = JsonSerializer.Serialize(toWrite, new JsonSerializerOptions { WriteIndented = false });
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] encrypted = Encrypt(jsonBytes);
        File.WriteAllBytes(TokenFilePath, encrypted);

        // Restrict file permissions on POSIX platforms so only the
        // owning user can read or write the file.
        SetPosixFilePermissions(TokenFilePath);
    }

    public void Clear()
    {
        if (File.Exists(TokenFilePath))
            File.Delete(TokenFilePath);

        string sentinelPath = TokenFilePath + _warningSentinelSuffix;
        if (File.Exists(sentinelPath))
            File.Delete(sentinelPath);
    }

    // -------------------------------------------------------------------------
    // Encryption helpers
    // -------------------------------------------------------------------------

    private static byte[] Encrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }

        // Linux / macOS: AES-256-GCM with a machine+user derived key.
        // Layout: [12-byte nonce][16-byte tag][ciphertext]
        byte[] key   = DeriveFileKey();
        byte[] nonce = new byte[AesNonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[data.Length];
        byte[] tag        = new byte[AesTagSize];

        using AesGcm aesGcm = new(key, AesTagSize);
        aesGcm.Encrypt(nonce, data, ciphertext, tag);

        // Combine into a single blob
        byte[] blob = new byte[AesNonceSize + AesTagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce,      0, blob, 0,                          AesNonceSize);
        Buffer.BlockCopy(tag,        0, blob, AesNonceSize,               AesTagSize);
        Buffer.BlockCopy(ciphertext, 0, blob, AesNonceSize + AesTagSize,  ciphertext.Length);
        return blob;
    }

    private static byte[] Decrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                // The file may have been written on a different machine or
                // by an older build. Treat it as unreadable rather than
                // falling back to plaintext.
                throw;
            }
        }

        // Linux / macOS: AES-256-GCM.
        if (data.Length < AesNonceSize + AesTagSize)
            throw new CryptographicException("Token cache blob is too short to be valid.");

        byte[] key   = DeriveFileKey();
        byte[] nonce = data[..AesNonceSize];
        byte[] tag   = data[AesNonceSize..(AesNonceSize + AesTagSize)];
        byte[] ciphertext = data[(AesNonceSize + AesTagSize)..];
        byte[] plaintext  = new byte[ciphertext.Length];

        using AesGcm aesGcm = new(key, AesTagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    // -------------------------------------------------------------------------
    // Key derivation — Linux / macOS fallback
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derives a 256-bit AES key from a machine-specific secret so that the
    /// encrypted file cannot be decrypted on a different machine.
    ///
    /// Key material: HMAC-SHA256(key = machine-id bytes, data = username UTF-8)
    ///
    /// /etc/machine-id is used on Linux. On macOS we use IOPlatformUUID via
    /// ioreg output if available; otherwise we fall back to the hostname.
    /// This is intentionally not a secret — the security guarantee is
    /// AES-GCM confidentiality and integrity, not key secrecy (which DPAPI
    /// provides on Windows). The real mitigation is that the refresh token
    /// is never stored in this path.
    /// </summary>
    internal static byte[] DeriveFileKey()
    {
        string machineId = GetMachineId();
        string username  = Environment.UserName;

        byte[] keyMaterial  = Encoding.UTF8.GetBytes(machineId);
        byte[] dataToHash   = Encoding.UTF8.GetBytes(username);

        using HMACSHA256 hmac = new(keyMaterial);
        return hmac.ComputeHash(dataToHash); // 32 bytes — exactly AES-256 key size
    }

    private static string GetMachineId()
    {
        // Linux
        if (File.Exists("/etc/machine-id"))
        {
            string id = File.ReadAllText("/etc/machine-id").Trim();
            if (!string.IsNullOrEmpty(id))
                return id;
        }

        // macOS — ioreg
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new("ioreg",
                    "-rd1 -c IOPlatformExpertDevice")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    // Extract IOPlatformUUID value
                    const string marker = "\"IOPlatformUUID\" = \"";
                    int start = output.IndexOf(marker, StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        start += marker.Length;
                        int end = output.IndexOf('"', start);
                        if (end > start)
                            return output[start..end];
                    }
                }
            }
            catch
            {
                // Fall through to hostname
            }
        }

        // Final fallback — hostname + username combination
        return $"{Environment.MachineName}:{Environment.UserName}";
    }

    // -------------------------------------------------------------------------
    // POSIX file permissions
    // -------------------------------------------------------------------------

    private static void SetPosixFilePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            // chmod 600 — owner read/write only
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Non-fatal: encryption still protects the content.
        }
    }

    // -------------------------------------------------------------------------
    // One-time keyring warning
    // -------------------------------------------------------------------------

    private void EmitKeyringWarningOnce(string dir)
    {
        string sentinelPath = TokenFilePath + _warningSentinelSuffix;
        if (File.Exists(sentinelPath))
            return;

        Console.Error.WriteLine(
            "[CloudSmith] Secure keyring unavailable — only access token cached. " +
            "Run 'cs auth login' when token expires.");

        try
        {
            File.WriteAllText(sentinelPath, DateTimeOffset.UtcNow.ToString("O"));
            SetPosixFilePermissions(sentinelPath);
        }
        catch
        {
            // Non-fatal — warning still printed.
        }
    }
}

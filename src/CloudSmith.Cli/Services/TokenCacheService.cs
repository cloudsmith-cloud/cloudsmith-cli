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

public sealed class TokenCacheService : ITokenCacheService
{
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
        return Path.Combine(baseDir, "tokens.json");
    }

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

        string json = JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true });
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] encrypted = Encrypt(jsonBytes);
        File.WriteAllBytes(TokenFilePath, encrypted);
    }

    public void Clear()
    {
        if (File.Exists(TokenFilePath))
            File.Delete(TokenFilePath);
    }

    private static byte[] Encrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }
        // On non-Windows: write plaintext. File permissions provide the security boundary.
        // Production hardening: use libsecret / Keychain via MSAL extension.
        return data;
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
                // Fall through — file may have been written without encryption (cross-platform migration)
                return data;
            }
        }
        return data;
    }
}

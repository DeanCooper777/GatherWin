using System.Security.Cryptography;
using System.Text;

namespace GatherWin.Services;

/// <summary>
/// Encrypts and decrypts strings using Windows DPAPI (Data Protection API).
/// Encryption is scoped to the current Windows user account.
/// </summary>
public static class CredentialProtector
{
    /// <summary>
    /// Encrypts a plaintext string using DPAPI and returns a Base64-encoded result.
    /// Returns empty string if input is null or empty.
    /// </summary>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encryptedBytes = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// Decrypts a Base64-encoded DPAPI-encrypted string back to plaintext.
    /// Returns empty string if input is null or empty.
    /// </summary>
    public static string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return string.Empty;

        var encryptedBytes = Convert.FromBase64String(encrypted);
        var plaintextBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>
    /// Returns true if the value looks like a plaintext API key (not yet encrypted).
    /// </summary>
    public static bool IsPlaintext(string value)
    {
        return value.StartsWith("sk-ant-", StringComparison.Ordinal);
    }
}

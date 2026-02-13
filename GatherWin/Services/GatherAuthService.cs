using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GatherWin.Models;
using NSec.Cryptography;

namespace GatherWin.Services;

/// <summary>
/// Handles Ed25519 challenge-response authentication with the Gather API.
/// Manages JWT token lifecycle with automatic refresh.
/// </summary>
public class GatherAuthService
{
    private const string GatherBaseUrl = "https://gather.is";
    private const int TokenRefreshBufferSeconds = 120;

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly string _publicKeyPem;
    private readonly byte[] _privateKeyBytes;

    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private bool _hasAuthenticatedOnce;

    public string? Token => _token;
    public DateTimeOffset TokenExpiry => _tokenExpiry;
    public bool IsAuthenticated => _token is not null && DateTimeOffset.UtcNow < _tokenExpiry;
    public bool KeysAvailable { get; }
    public string KeysError { get; } = string.Empty;

    /// <summary>Raised when the JWT token is automatically refreshed (not on initial auth).</summary>
    public event EventHandler<DateTimeOffset>? TokenRefreshed;

    public GatherAuthService(HttpClient http, JsonSerializerOptions jsonOpts, string keysDirectory)
    {
        _http = http;
        _jsonOpts = jsonOpts;

        if (string.IsNullOrEmpty(keysDirectory))
            keysDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gather");

        AppLogger.Log("Auth", $"Keys directory: {keysDirectory}");

        var privateKeyPath = Path.Combine(keysDirectory, "private.key");
        var publicKeyPemPath = Path.Combine(keysDirectory, "public.pem");

        if (!File.Exists(privateKeyPath))
        {
            KeysAvailable = false;
            KeysError = $"Private key not found at {privateKeyPath}";
            AppLogger.LogError($"Auth: {KeysError}");
            _publicKeyPem = string.Empty;
            _privateKeyBytes = [];
            return;
        }

        if (!File.Exists(publicKeyPemPath))
        {
            KeysAvailable = false;
            KeysError = $"Public key not found at {publicKeyPemPath}";
            AppLogger.LogError($"Auth: {KeysError}");
            _publicKeyPem = string.Empty;
            _privateKeyBytes = [];
            return;
        }

        _privateKeyBytes = File.ReadAllBytes(privateKeyPath);
        _publicKeyPem = File.ReadAllText(publicKeyPemPath).Trim();
        KeysAvailable = true;
        AppLogger.Log("Auth", $"Keys loaded successfully (public key: {_publicKeyPem[..Math.Min(40, _publicKeyPem.Length)]}...)");
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (!KeysAvailable)
            throw new InvalidOperationException($"Cannot authenticate: {KeysError}");

        if (_token is not null &&
            DateTimeOffset.UtcNow < _tokenExpiry - TimeSpan.FromSeconds(TokenRefreshBufferSeconds))
        {
            return;
        }

        AppLogger.Log("Auth", _token is null ? "Authenticating (first time)..." : "Refreshing token...");

        // Phase 1: Request challenge nonce
        var challengeReq = new ChallengeRequest { PublicKey = _publicKeyPem };
        var challengeResp = await _http.PostAsJsonAsync($"{GatherBaseUrl}/api/agents/challenge", challengeReq, _jsonOpts, ct);
        var challengeBody = await challengeResp.Content.ReadAsStringAsync(ct);

        if (!challengeResp.IsSuccessStatusCode)
        {
            AppLogger.LogError($"Auth: Challenge failed ({challengeResp.StatusCode}): {challengeBody}");
            throw new HttpRequestException($"Challenge failed ({challengeResp.StatusCode}): {challengeBody}");
        }

        AppLogger.Log("Auth", "Challenge received, signing nonce...");

        var challenge = JsonSerializer.Deserialize<ChallengeResponse>(challengeBody, _jsonOpts)
            ?? throw new InvalidOperationException("Failed to parse challenge response");

        // Phase 2: Sign nonce (sync helper for C# 12 ref-in-async limitation)
        var nonceRawBytes = Convert.FromBase64String(challenge.Nonce!);
        var signatureBytes = SignNonce(_privateKeyBytes, nonceRawBytes);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // Phase 3: Authenticate
        var authReq = new AuthenticateRequest { PublicKey = _publicKeyPem, Signature = signatureBase64 };
        var authResp = await _http.PostAsJsonAsync($"{GatherBaseUrl}/api/agents/authenticate", authReq, _jsonOpts, ct);
        var authBody = await authResp.Content.ReadAsStringAsync(ct);

        if (!authResp.IsSuccessStatusCode)
        {
            AppLogger.LogError($"Auth: Authentication failed ({authResp.StatusCode}): {authBody}");
            throw new HttpRequestException($"Auth failed ({authResp.StatusCode}): {authBody}");
        }

        var auth = JsonSerializer.Deserialize<AuthenticateResponse>(authBody, _jsonOpts)
            ?? throw new InvalidOperationException("Failed to parse auth response");

        _token = auth.Token;
        _tokenExpiry = ParseJwtExpiry(auth.Token!);

        AppLogger.Log("Auth", $"Authenticated successfully, token expires {_tokenExpiry.ToLocalTime():HH:mm:ss}");

        if (_hasAuthenticatedOnce)
            TokenRefreshed?.Invoke(this, _tokenExpiry);
        _hasAuthenticatedOnce = true;
    }

    /// <summary>
    /// Synchronous Ed25519 signing — separated from async to avoid C# 12 ref-in-async issue.
    /// </summary>
    private static byte[] SignNonce(byte[] privateKeyBytes, byte[] nonceBytes)
    {
        using var key = Key.Import(
            SignatureAlgorithm.Ed25519,
            privateKeyBytes,
            KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return SignatureAlgorithm.Ed25519.Sign(key, nonceBytes);
    }

    public static DateTimeOffset ParseJwtExpiry(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return DateTimeOffset.UtcNow.AddMinutes(30);

        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var doc = JsonDocument.Parse(json);
        var exp = doc.RootElement.GetProperty("exp").GetInt64();
        return DateTimeOffset.FromUnixTimeSeconds(exp);
    }
}

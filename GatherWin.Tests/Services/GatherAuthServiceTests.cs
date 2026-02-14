using System.Net.Http;
using GatherWin.Services;

namespace GatherWin.Tests.Services;

public class GatherAuthServiceTests
{
    [Fact]
    public void ParseJwtExpiry_ValidToken_ReturnsCorrectExpiry()
    {
        // Build a JWT with exp = 1700000000 (2023-11-14T22:13:20Z)
        var header = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode("{\"sub\":\"test\",\"exp\":1700000000}");
        var jwt = $"{header}.{payload}.fake-signature";

        var expiry = GatherAuthService.ParseJwtExpiry(jwt);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), expiry);
    }

    [Fact]
    public void ParseJwtExpiry_MalformedToken_ReturnsFallback()
    {
        // Single segment — no dots
        var expiry = GatherAuthService.ParseJwtExpiry("not-a-jwt");

        // Should return ~30 minutes from now as fallback
        var delta = expiry - DateTimeOffset.UtcNow;
        Assert.True(delta.TotalMinutes > 28 && delta.TotalMinutes < 32,
            $"Expected ~30 min fallback, got {delta.TotalMinutes:F1} min");
    }

    [Fact]
    public void ParseJwtExpiry_EmptyPayload_Throws()
    {
        // Two dots but empty payload — can't parse empty JSON
        Assert.ThrowsAny<Exception>(() => GatherAuthService.ParseJwtExpiry("header..signature"));
    }

    [Fact]
    public void EnsureAuthenticatedAsync_NoKeys_Throws()
    {
        // Use a non-existent keys directory so KeysAvailable = false
        var http = new HttpClient();
        var jsonOpts = new System.Text.Json.JsonSerializerOptions();
        var auth = new GatherAuthService(http, jsonOpts, "/nonexistent/path/to/keys");

        Assert.False(auth.KeysAvailable);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.EnsureAuthenticatedAsync(CancellationToken.None));
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

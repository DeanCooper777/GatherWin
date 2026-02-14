using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GatherWin.Models;

namespace GatherWin.Services;

/// <summary>
/// Gather API client for all read and write operations.
/// Delegates authentication to <see cref="GatherAuthService"/>.
/// </summary>
public class GatherApiClient
{
    private const string GatherBaseUrl = "https://gather.is";

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly GatherAuthService _auth;

    public GatherApiClient(HttpClient http, JsonSerializerOptions jsonOpts, GatherAuthService auth)
    {
        _http = http;
        _jsonOpts = jsonOpts;
        _auth = auth;
    }

    // ── Authenticated HTTP helpers ──────────────────────────────

    private async Task<HttpResponseMessage?> AuthenticatedGetAsync(string url, CancellationToken ct)
    {
        try
        {
            await _auth.EnsureAuthenticatedAsync(ct);

            AppLogger.Log("API", $"GET {url}");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GatherBaseUrl}{url}");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _auth.Token);

            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AppLogger.Log("API", $"GET {url} → 401, re-authenticating...");
                await _auth.EnsureAuthenticatedAsync(ct);
                using var retry = new HttpRequestMessage(HttpMethod.Get, $"{GatherBaseUrl}{url}");
                retry.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _auth.Token);
                response = await _http.SendAsync(retry, ct);
            }

            AppLogger.Log("API", $"GET {url} → {(int)response.StatusCode}");
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError($"API: GET {url} failed", ex);
            return null;
        }
    }

    private async Task<HttpResponseMessage?> AuthenticatedPostAsync(
        string url, object body, CancellationToken ct)
    {
        try
        {
            await _auth.EnsureAuthenticatedAsync(ct);

            AppLogger.Log("API", $"POST {url}");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{GatherBaseUrl}{url}");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _auth.Token);
            request.Content = JsonContent.Create(body, options: _jsonOpts);

            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AppLogger.Log("API", $"POST {url} → 401, re-authenticating...");
                await _auth.EnsureAuthenticatedAsync(ct);
                using var retry = new HttpRequestMessage(HttpMethod.Post, $"{GatherBaseUrl}{url}");
                retry.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _auth.Token);
                retry.Content = JsonContent.Create(body, options: _jsonOpts);
                response = await _http.SendAsync(retry, ct);
            }

            AppLogger.Log("API", $"POST {url} → {(int)response.StatusCode}");
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError($"API: POST {url} failed", ex);
            return null;
        }
    }

    // ── Read Methods ────────────────────────────────────────────

    public async Task<GatherPost?> GetPostWithCommentsAsync(string postId, CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync($"/api/posts/{postId}?expand=comments", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GatherPost>(body, _jsonOpts);
    }

    public async Task<InboxResponse?> GetInboxAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/inbox", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<InboxResponse>(body, _jsonOpts);
    }

    public async Task<FeedResponse?> GetFeedPostsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var url = "/api/posts?limit=50";
        if (since.HasValue)
            url += $"&since={since.Value.UtcDateTime:O}";
        var response = await AuthenticatedGetAsync(url, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<FeedResponse>(body, _jsonOpts);
    }

    public async Task<ChannelListResponse?> GetChannelsAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/channels", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ChannelListResponse>(body, _jsonOpts);
    }

    public async Task<ChannelMessageListResponse?> GetChannelMessagesAsync(
        string channelId, DateTimeOffset? since, CancellationToken ct)
    {
        var url = $"/api/channels/{channelId}/messages?limit=50";
        if (since.HasValue)
            url += $"&since={since.Value.UtcDateTime:O}";
        var response = await AuthenticatedGetAsync(url, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ChannelMessageListResponse>(body, _jsonOpts);
    }

    public async Task<BalanceResponse?> GetBalanceAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/balance", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<BalanceResponse>(body, _jsonOpts);
    }

    // ── Agent Methods (Features 9 & 10) ──────────────────────────

    /// <summary>Get a specific agent by ID.</summary>
    public async Task<AgentItem?> GetAgentByIdAsync(string agentId, CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync($"/api/agents/{agentId}", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<AgentItem>(body, _jsonOpts);
    }

    /// <summary>Search for an agent by name (searches the agents list).</summary>
    public async Task<AgentItem?> GetAgentByNameAsync(string name, CancellationToken ct)
    {
        var agents = await GetAgentsAsync(ct);
        return agents?.Agents?.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ── Discovery / What's New Methods ───────────────────────────

    /// <summary>Top posts from last 24 hours (daily digest).</summary>
    public async Task<DigestResponse?> GetDigestAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/posts/digest", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<DigestResponse>(body, _jsonOpts);
    }

    /// <summary>Posts tagged 'platform' — official announcements.</summary>
    public async Task<FeedResponse?> GetPlatformPostsAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/posts?tag=platform&sort=newest&limit=20", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<FeedResponse>(body, _jsonOpts);
    }

    /// <summary>Skills marketplace listing.</summary>
    public async Task<SkillsResponse?> GetSkillsAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/skills", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<SkillsResponse>(body, _jsonOpts);
    }

    /// <summary>Agent directory listing.</summary>
    public async Task<AgentsResponse?> GetAgentsAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/agents", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<AgentsResponse>(body, _jsonOpts);
    }

    /// <summary>Current fee schedule.</summary>
    public async Task<FeeScheduleResponse?> GetFeeScheduleAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/balance/fees", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<FeeScheduleResponse>(body, _jsonOpts);
    }

    /// <summary>Fetches the raw OpenAPI spec document as a string.</summary>
    public async Task<string?> GetOpenApiSpecAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/openapi.json", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(ct);
    }

    // ── Write Methods ───────────────────────────────────────────

    /// <summary>Posts a comment on a Gather post. Returns true on success.</summary>
    /// <param name="replyToCommentId">Optional comment ID to reply to (for threaded discussions).</param>
    public async Task<(bool Success, string? Error)> PostCommentAsync(
        string postId, string commentBody, CancellationToken ct, string? replyToCommentId = null)
    {
        var payload = new Dictionary<string, string> { ["body"] = commentBody };
        if (!string.IsNullOrEmpty(replyToCommentId))
            payload["reply_to"] = replyToCommentId;
        var response = await AuthenticatedPostAsync($"/api/posts/{postId}/comments", payload, ct);

        if (response is null)
            return (false, "Network error");

        if (response.IsSuccessStatusCode)
            return (true, null);

        // If 402, try with PoW
        if (response.StatusCode == HttpStatusCode.PaymentRequired)
            return await PostWithPoWAsync($"/api/posts/{postId}/comments", payload, "comment", ct);

        var error = await response.Content.ReadAsStringAsync(ct);
        AppLogger.LogError($"API: POST /api/posts/{postId}/comments failed → HTTP {(int)response.StatusCode}: {error}");
        return (false, $"HTTP {(int)response.StatusCode}: {error}");
    }

    /// <summary>Creates a new post on the Gather feed. Returns the post ID on success.</summary>
    public async Task<(bool Success, string? PostId, string? Error)> CreatePostAsync(
        string title, string body, List<string> tags, CancellationToken ct)
    {
        try
        {
            // Posts always require PoW — get challenge first
            AppLogger.Log("API", "Getting PoW challenge for post creation...");
            var challengePayload = new Dictionary<string, string> { ["purpose"] = "post" };
            var challengeResp = await AuthenticatedPostAsync("/api/pow/challenge", challengePayload, ct);
            if (challengeResp is null || !challengeResp.IsSuccessStatusCode)
                return (false, null, "Failed to get PoW challenge");

            var challengeBody = await challengeResp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(challengeBody);
            var challenge = doc.RootElement.GetProperty("challenge").GetString()!;
            var difficulty = doc.RootElement.GetProperty("difficulty").GetInt32();

            AppLogger.Log("API", $"Solving PoW (difficulty={difficulty})...");
            var (_, nonce) = ProofOfWorkSolver.Solve(challenge, difficulty);
            AppLogger.Log("API", "PoW solved, creating post...");

            // summary is required — use truncated body
            var summary = body.Length <= 280 ? body : body[..277] + "...";

            var payload = new Dictionary<string, object>
            {
                ["title"] = title,
                ["body"] = body,
                ["summary"] = summary,
                ["tags"] = tags,
                ["pow_challenge"] = challenge,
                ["pow_nonce"] = nonce
            };
            var response = await AuthenticatedPostAsync("/api/posts", payload, ct);

            if (response is null)
                return (false, null, "Network error");

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                try
                {
                    var post = JsonSerializer.Deserialize<GatherPost>(responseBody, _jsonOpts);
                    return (true, post?.Id, null);
                }
                catch
                {
                    return (true, null, null); // Created but couldn't parse response
                }
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            AppLogger.LogError($"API: POST /api/posts failed → HTTP {(int)response.StatusCode}: {error}");
            return (false, null, $"HTTP {(int)response.StatusCode}: {error}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (false, null, $"Error creating post: {ex.Message}");
        }
    }

    /// <summary>Creates a new channel. Returns the channel ID on success.</summary>
    public async Task<(bool Success, string? ChannelId, string? Error)> CreateChannelAsync(
        string name, string? description, CancellationToken ct)
    {
        var payload = new Dictionary<string, string?> { ["name"] = name, ["description"] = description };
        var response = await AuthenticatedPostAsync("/api/channels", payload, ct);

        if (response is null)
            return (false, null, "Network error");

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var channel = JsonSerializer.Deserialize<ChannelItem>(body, _jsonOpts);
                return (true, channel?.Id, null);
            }
            catch
            {
                return (true, null, null); // Created but couldn't parse response
            }
        }

        var error = await response.Content.ReadAsStringAsync(ct);
        AppLogger.LogError($"API: POST /api/channels failed → HTTP {(int)response.StatusCode}: {error}");
        return (false, null, $"HTTP {(int)response.StatusCode}: {error}");
    }

    /// <summary>Posts a message to a Gather channel. Returns true on success.</summary>
    public async Task<(bool Success, string? Error)> PostChannelMessageAsync(
        string channelId, string messageBody, CancellationToken ct, string? replyToMessageId = null)
    {
        var payload = new Dictionary<string, string> { ["body"] = messageBody };
        if (!string.IsNullOrEmpty(replyToMessageId))
            payload["reply_to"] = replyToMessageId;
        var response = await AuthenticatedPostAsync($"/api/channels/{channelId}/messages", payload, ct);

        if (response is null)
            return (false, "Network error");

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
            return await PostWithPoWAsync($"/api/channels/{channelId}/messages", payload, "comment", ct);

        var error = await response.Content.ReadAsStringAsync(ct);
        AppLogger.LogError($"API: POST /api/channels/{channelId}/messages failed → HTTP {(int)response.StatusCode}: {error}");
        return (false, $"HTTP {(int)response.StatusCode}: {error}");
    }

    private async Task<(bool Success, string? Error)> PostWithPoWAsync(
        string url, Dictionary<string, string> payload, string purpose, CancellationToken ct)
    {
        try
        {
            AppLogger.Log("API", $"PoW required for {url}, getting challenge...");

            // Get PoW challenge
            var challengePayload = new Dictionary<string, string> { ["purpose"] = purpose };
            var challengeResp = await AuthenticatedPostAsync("/api/pow/challenge", challengePayload, ct);
            if (challengeResp is null || !challengeResp.IsSuccessStatusCode)
                return (false, "Failed to get PoW challenge");

            var challengeBody = await challengeResp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(challengeBody);
            var challenge = doc.RootElement.GetProperty("challenge").GetString()!;
            var difficulty = doc.RootElement.GetProperty("difficulty").GetInt32();

            AppLogger.Log("API", $"Solving PoW (difficulty={difficulty})...");

            // Solve PoW
            var (_, nonce) = ProofOfWorkSolver.Solve(challenge, difficulty);

            AppLogger.Log("API", $"PoW solved, retrying POST {url}...");

            // Retry with PoW
            var powPayload = new Dictionary<string, object>(
                payload.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)))
            {
                ["pow_challenge"] = challenge,
                ["pow_nonce"] = nonce
            };

            var response = await AuthenticatedPostAsync(url, powPayload, ct);
            if (response is null)
                return (false, "Network error after PoW");

            if (response.IsSuccessStatusCode)
                return (true, null);

            var error = await response.Content.ReadAsStringAsync(ct);
            return (false, $"HTTP {(int)response.StatusCode}: {error}");
        }
        catch (Exception ex)
        {
            return (false, $"PoW error: {ex.Message}");
        }
    }
}

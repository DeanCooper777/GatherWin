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

    /// <summary>
    /// When true, list endpoints request full post bodies via expand=body.
    /// When false (default), the API returns headlines/summaries only.
    /// </summary>
    public bool ShowFullPosts { get; set; }

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

    private async Task<HttpResponseMessage?> AuthenticatedPutAsync(string url, CancellationToken ct)
    {
        try
        {
            await _auth.EnsureAuthenticatedAsync(ct);
            AppLogger.Log("API", $"PUT {url}");
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{GatherBaseUrl}{url}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
            var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await _auth.EnsureAuthenticatedAsync(ct);
                using var retry = new HttpRequestMessage(HttpMethod.Put, $"{GatherBaseUrl}{url}");
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
                response = await _http.SendAsync(retry, ct);
            }
            AppLogger.Log("API", $"PUT {url} → {(int)response.StatusCode}");
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppLogger.LogError($"API: PUT {url} failed", ex); return null; }
    }

    private async Task<HttpResponseMessage?> AuthenticatedDeleteAsync(string url, CancellationToken ct)
    {
        try
        {
            await _auth.EnsureAuthenticatedAsync(ct);
            AppLogger.Log("API", $"DELETE {url}");
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{GatherBaseUrl}{url}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
            var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await _auth.EnsureAuthenticatedAsync(ct);
                using var retry = new HttpRequestMessage(HttpMethod.Delete, $"{GatherBaseUrl}{url}");
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
                response = await _http.SendAsync(retry, ct);
            }
            AppLogger.Log("API", $"DELETE {url} → {(int)response.StatusCode}");
            return response;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppLogger.LogError($"API: DELETE {url} failed", ex); return null; }
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
        var response = await AuthenticatedGetAsync($"/api/posts/{postId}?expand=comments,body", ct);
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

    /// <summary>Fast unread count endpoint — lightweight alternative to fetching full inbox.</summary>
    public async Task<int?> GetInboxUnreadCountAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/inbox/unread", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<InboxUnreadResponse>(body, _jsonOpts);
        return result?.Unread;
    }

    /// <summary>Mark an inbox message as read.</summary>
    public async Task<(bool Success, string? Error)> MarkInboxReadAsync(string messageId, CancellationToken ct)
    {
        var response = await AuthenticatedPutAsync($"/api/inbox/{messageId}/read", ct);
        if (response is null) return (false, "Network error");
        if (response.IsSuccessStatusCode) return (true, null);
        var error = await response.Content.ReadAsStringAsync(ct);
        return (false, ParseApiError(error, (int)response.StatusCode));
    }

    /// <summary>Delete an inbox message.</summary>
    public async Task<(bool Success, string? Error)> DeleteInboxMessageAsync(string messageId, CancellationToken ct)
    {
        var response = await AuthenticatedDeleteAsync($"/api/inbox/{messageId}", ct);
        if (response is null) return (false, "Network error");
        if (response.IsSuccessStatusCode) return (true, null);
        var error = await response.Content.ReadAsStringAsync(ct);
        return (false, ParseApiError(error, (int)response.StatusCode));
    }

    public async Task<FeedResponse?> GetFeedPostsAsync(DateTimeOffset? since, CancellationToken ct,
        string sort = "newest", string? searchQuery = null, string? tag = null)
    {
        var url = $"/api/posts?limit=50&sort={sort}";
        if (since.HasValue)
            url += $"&since={since.Value.UtcDateTime:O}";
        if (!string.IsNullOrWhiteSpace(searchQuery))
            url += $"&q={Uri.EscapeDataString(searchQuery)}";
        if (!string.IsNullOrWhiteSpace(tag))
            url += $"&tag={Uri.EscapeDataString(tag)}";
        if (ShowFullPosts)
            url += "&expand=body";
        var response = await AuthenticatedGetAsync(url, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<FeedResponse>(body, _jsonOpts);
    }

    /// <summary>Get active tags from the last 30 days.</summary>
    public async Task<TagsResponse?> GetTagsAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/tags", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TagsResponse>(body, _jsonOpts);
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

    public async Task<FeesResponse?> GetFeesAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/balance/fees", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<FeesResponse>(body, _jsonOpts);
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

    /// <summary>Get posts by a specific agent. Matches by Author name or AuthorId.</summary>
    public async Task<List<GatherPost>> GetPostsByAgentAsync(string agentName, string? agentId, int limit, CancellationToken ct)
    {
        // Fetch posts and filter client-side (API doesn't support author filtering).
        // API max limit is 100, so paginate if needed to find enough matches.
        var results = new List<GatherPost>();
        var offset = 0;
        const int pageSize = 100;
        const int maxPages = 5; // Don't fetch more than 500 posts total

        for (var page = 0; page < maxPages && results.Count < limit; page++)
        {
            var url = $"/api/posts?limit={pageSize}&offset={offset}&sort=newest";
            if (ShowFullPosts)
                url += "&expand=body";

            var response = await AuthenticatedGetAsync(url, ct);
            if (response is null || !response.IsSuccessStatusCode) break;

            var body = await response.Content.ReadAsStringAsync(ct);
            var feed = JsonSerializer.Deserialize<FeedResponse>(body, _jsonOpts);
            if (feed?.Posts is null || feed.Posts.Count == 0) break;

            if (page == 0)
            {
                AppLogger.Log("API", $"GetPostsByAgent: searching for agentName=\"{agentName}\" agentId=\"{agentId}\" in up to {feed.Total} total posts");
                var sample = feed.Posts.Take(5).Select(p => $"Author=\"{p.Author}\" AuthorId=\"{p.AuthorId}\"");
                AppLogger.Log("API", $"GetPostsByAgent: sample authors: {string.Join("; ", sample)}");
            }

            var matches = feed.Posts
                .Where(p =>
                    string.Equals(p.Author, agentName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(agentId) && string.Equals(p.AuthorId, agentId, StringComparison.OrdinalIgnoreCase)));

            results.AddRange(matches);
            offset += pageSize;

            // If we got fewer than a full page, there are no more posts
            if (feed.Posts.Count < pageSize) break;
        }

        AppLogger.Log("API", $"GetPostsByAgent: found {results.Count} matching posts (limit={limit})");
        return results.Take(limit).ToList();
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
        var url = "/api/posts?tag=platform&sort=newest&limit=20";
        if (ShowFullPosts)
            url += "&expand=body";
        var response = await AuthenticatedGetAsync(url, ct);
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
        return (false, ParseApiError(error, (int)response.StatusCode));
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
                catch (Exception ex)
                {
                    AppLogger.LogError("API: response parse failed for CreatePost", ex);
                    return (true, null, null); // Created but couldn't parse response
                }
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            AppLogger.LogError($"API: POST /api/posts failed → HTTP {(int)response.StatusCode}: {error}");
            return (false, null, ParseApiError(error, (int)response.StatusCode));
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
            catch (Exception ex)
            {
                AppLogger.LogError("API: response parse failed for CreateChannel", ex);
                return (true, null, null); // Created but couldn't parse response
            }
        }

        var error = await response.Content.ReadAsStringAsync(ct);
        AppLogger.LogError($"API: POST /api/channels failed → HTTP {(int)response.StatusCode}: {error}");
        return (false, null, ParseApiError(error, (int)response.StatusCode));
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
        return (false, ParseApiError(error, (int)response.StatusCode));
    }

    /// <summary>Vote on a post. value: 1 (upvote), -1 (downvote), 0 (remove). Returns new score.</summary>
    public async Task<(bool Success, int NewScore, string? Error)> VotePostAsync(
        string postId, int value, CancellationToken ct)
    {
        var payload = new Dictionary<string, string> { ["value"] = value.ToString() };
        var response = await AuthenticatedPostAsync($"/api/posts/{postId}/vote", payload, ct);

        if (response is null)
            return (false, 0, "Network error");

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var result = JsonSerializer.Deserialize<VoteResponse>(body, _jsonOpts);
                return (true, result?.NewScore ?? 0, null);
            }
            catch
            {
                return (true, 0, null);
            }
        }

        var error = await response.Content.ReadAsStringAsync(ct);
        AppLogger.LogError($"API: POST /api/posts/{postId}/vote failed → HTTP {(int)response.StatusCode}: {error}");
        return (false, 0, ParseApiError(error, (int)response.StatusCode));
    }

    // ── Feedback ──────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> SubmitFeedbackAsync(
        int rating, string? message, CancellationToken ct)
    {
        var payload = new Dictionary<string, object> { ["rating"] = rating };
        if (!string.IsNullOrWhiteSpace(message))
            payload["message"] = message.Trim();

        var response = await AuthenticatedPostAsync("/api/feedback", payload, ct);
        if (response is null)
            return (false, "Network error");

        if (response.IsSuccessStatusCode)
            return (true, null);

        var error = await response.Content.ReadAsStringAsync(ct);
        return (false, ParseApiError(error, (int)response.StatusCode));
    }

    // ── Tipping ─────────────────────────────────────────────────

    public async Task<(bool Success, TipResponse? Result, string? Error)> TipAgentAsync(
        string toAgentId, decimal amountBch, string? message, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["to"] = toAgentId,
            ["amount_bch"] = amountBch
        };
        if (!string.IsNullOrWhiteSpace(message))
            payload["message"] = message.Trim();

        var response = await AuthenticatedPostAsync("/api/balance/tip", payload, ct);
        if (response is null)
            return (false, null, "Network error");

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var result = JsonSerializer.Deserialize<TipResponse>(body, _jsonOpts);
                return (true, result, null);
            }
            catch
            {
                return (true, null, null);
            }
        }

        var error = await response.Content.ReadAsStringAsync(ct);
        return (false, null, ParseApiError(error, (int)response.StatusCode));
    }

    // ── Reviews ──────────────────────────────────────────────────

    public async Task<ReviewsResponse?> GetReviewsAsync(CancellationToken ct, string? skillId = null)
    {
        var url = "/api/reviews";
        if (!string.IsNullOrEmpty(skillId))
            url += $"?skill_id={Uri.EscapeDataString(skillId)}";
        var response = await AuthenticatedGetAsync(url, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ReviewsResponse>(body, _jsonOpts);
    }

    public async Task<ReviewItem?> GetReviewDetailAsync(string reviewId, CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync($"/api/reviews/{reviewId}", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ReviewItem>(body, _jsonOpts);
    }

    public async Task<(bool Success, SubmitReviewResponse? Result, string? Error)> SubmitReviewAsync(
        string skillId, string task, int score, string? whatWorked, string? whatFailed,
        string? skillFeedback, int? securityScore, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["skill_id"] = skillId,
            ["task"] = task,
            ["score"] = score
        };
        if (!string.IsNullOrWhiteSpace(whatWorked)) payload["what_worked"] = whatWorked.Trim();
        if (!string.IsNullOrWhiteSpace(whatFailed)) payload["what_failed"] = whatFailed.Trim();
        if (!string.IsNullOrWhiteSpace(skillFeedback)) payload["skill_feedback"] = skillFeedback.Trim();
        if (securityScore.HasValue) payload["security_score"] = securityScore.Value;

        var response = await AuthenticatedPostAsync("/api/reviews/submit", payload, ct);
        if (response is null) return (false, null, "Network error");

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var result = JsonSerializer.Deserialize<SubmitReviewResponse>(body, _jsonOpts);
                return (true, result, null);
            }
            catch { return (true, null, null); }
        }

        var error = await response.Content.ReadAsStringAsync(ct);
        return (false, null, ParseApiError(error, (int)response.StatusCode));
    }

    // ── Shop / Menu ─────────────────────────────────────────────

    public async Task<MenuResponse?> GetMenuAsync(CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync("/api/menu", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<MenuResponse>(body, _jsonOpts);
    }

    public async Task<CategoryItemsResponse?> GetMenuCategoryAsync(string category, CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync($"/api/menu/{Uri.EscapeDataString(category)}", ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CategoryItemsResponse>(body, _jsonOpts);
    }

    // ── Channel Invitations ─────────────────────────────────────

    public async Task<(bool Success, string? Error)> InviteToChannelAsync(
        string channelId, string agentId, CancellationToken ct)
    {
        var payload = new Dictionary<string, object> { ["agent_id"] = agentId };
        var response = await AuthenticatedPostAsync($"/api/channels/{channelId}/invite", payload, ct);
        if (response is null)
            return (false, "Network error");

        if (response.IsSuccessStatusCode)
            return (true, null);

        var error = await response.Content.ReadAsStringAsync(ct);
        return (false, ParseApiError(error, (int)response.StatusCode));
    }

    // ── Channel Details ─────────────────────────────────────────

    public async Task<ChannelDetail?> GetChannelDetailAsync(string channelId, CancellationToken ct)
    {
        var response = await AuthenticatedGetAsync($"/api/channels/{channelId}", ct);
        if (response is null || !response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ChannelDetail>(body, _jsonOpts);
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
            return (false, ParseApiError(error, (int)response.StatusCode));
        }
        catch (Exception ex)
        {
            return (false, $"PoW error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a human-readable error message from the Gather API's JSON error response.
    /// Falls back to a generic "HTTP {status}" message if parsing fails.
    /// </summary>
    private static string ParseApiError(string responseBody, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Try to extract validation error messages: { "errors": [{ "message": "..." }] }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = new List<string>();
                foreach (var err in errors.EnumerateArray())
                {
                    if (err.TryGetProperty("message", out var msg))
                        messages.Add(msg.GetString() ?? "");
                }
                if (messages.Count > 0)
                    return string.Join("; ", messages);
            }

            // Try "detail" field
            if (root.TryGetProperty("detail", out var detail))
                return detail.GetString() ?? $"HTTP {statusCode}";

            // Try "title" field
            if (root.TryGetProperty("title", out var title))
                return title.GetString() ?? $"HTTP {statusCode}";
        }
        catch
        {
            // Not valid JSON — return raw body (truncated)
        }

        return responseBody.Length > 200
            ? $"HTTP {statusCode}: {responseBody[..200]}..."
            : $"HTTP {statusCode}: {responseBody}";
    }
}

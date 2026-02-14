using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GatherWin.Services;

/// <summary>
/// Client for the Anthropic Claude Messages API.
/// Used for AI writing assistance.
/// </summary>
public class ClaudeApiClient : IDisposable
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public ClaudeApiClient(string apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>
    /// Ask Claude to rewrite text with a given creativity level and optional thread context.
    /// </summary>
    /// <param name="originalText">The user's draft text.</param>
    /// <param name="creativityLevel">0.0 (mild/conservative) to 1.0 (wild/creative).</param>
    /// <param name="threadContext">Optional surrounding conversation for context.</param>
    /// <returns>The AI-modified text, or an error message.</returns>
    public async Task<(bool Success, string Result)> AssistWritingAsync(
        string originalText, double creativityLevel, string? threadContext, CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, "Claude API key not configured. Add it in Options.");

        try
        {
            var style = creativityLevel < 0.3 ? "minor grammar and clarity improvements only"
                : creativityLevel < 0.7 ? "moderate improvements to style, clarity, and engagement"
                : "creative rewrite — make it more engaging, witty, and polished while preserving meaning";

            var systemPrompt = "You are a writing assistant for a social media platform. " +
                "Rewrite the user's text with the following style: " + style + ". " +
                "Return ONLY the rewritten text, no explanations or markup.";

            var userMessage = originalText;
            if (!string.IsNullOrEmpty(threadContext))
                userMessage = $"Thread context:\n{threadContext}\n\n---\nText to rewrite:\n{originalText}";

            var payload = new
            {
                model = "claude-sonnet-4-5-20250929",
                max_tokens = 1024,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.LogError($"Claude API: HTTP {(int)response.StatusCode}: {responseBody}");
                return (false, $"Claude API error: HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement.GetProperty("content");
            var text = content[0].GetProperty("text").GetString() ?? "";
            return (true, text);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("Claude API call failed", ex);
            return (false, $"Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

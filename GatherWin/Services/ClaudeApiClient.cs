using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GatherWin.Models;

namespace GatherWin.Services;

/// <summary>
/// Client for the Anthropic Claude Messages API.
/// Used for AI writing assistance and direct Claude conversations.
/// </summary>
public class ClaudeApiClient : IDisposable
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public ClaudeApiClient(string apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
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
            request.Headers.Add("anthropic-version", AnthropicVersion);
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

    // ── Direct streaming chat ─────────────────────────────────────────

    /// <summary>
    /// Send a multi-turn conversation to Claude and stream the reply chunk by chunk.
    /// </summary>
    /// <returns>Success flag, full reply text, error message, and output token count.</returns>
    public async Task<(bool Success, string? Reply, string? Error, int OutputTokens)> StreamChatAsync(
        IReadOnlyList<ClaudeChatEntry> history,
        string? systemPrompt,
        string model,
        Action<string> onChunk,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, null, "Claude API key not configured. Add it in Options.", 0);

        try
        {
            var messages = history.Select(e => new { role = e.Role, content = e.Content }).ToArray();
            var payloadDict = new Dictionary<string, object>
            {
                ["model"]      = model,
                ["max_tokens"] = 8192,
                ["stream"]     = true,
                ["messages"]   = messages
            };
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                payloadDict["system"] = systemPrompt;

            var json = JsonSerializer.Serialize(payloadDict);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            AppLogger.Log("ClaudeChat", $"POST /messages → HTTP {(int)response.StatusCode} model={model}");

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Log("ClaudeChat", $"Error: {errBody}");
                // Try to extract a readable error message
                try
                {
                    using var errDoc = JsonDocument.Parse(errBody);
                    if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                        errEl.TryGetProperty("message", out var msgEl))
                        return (false, null, msgEl.GetString() ?? errBody, 0);
                }
                catch { }
                return (false, null, $"API error HTTP {(int)response.StatusCode}", 0);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);
            var reply = new StringBuilder();
            int outputTokens = 0;
            string? currentEventType = null;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                if (line.StartsWith("event:"))
                {
                    currentEventType = line["event:".Length..].Trim();
                    continue;
                }

                if (!line.StartsWith("data:")) continue;
                var data = line["data:".Length..].Trim();
                if (data == "[DONE]" || string.IsNullOrEmpty(data)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;
                    var eventType = root.TryGetProperty("type", out var typeEl)
                        ? typeEl.GetString() : currentEventType;

                    if (eventType == "content_block_delta")
                    {
                        if (root.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("type", out var deltaType) &&
                            deltaType.GetString() == "text_delta" &&
                            delta.TryGetProperty("text", out var textEl))
                        {
                            var chunk = textEl.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                reply.Append(chunk);
                                onChunk(chunk);
                            }
                        }
                    }
                    else if (eventType == "message_delta")
                    {
                        if (root.TryGetProperty("usage", out var usage) &&
                            usage.TryGetProperty("output_tokens", out var tokEl))
                            outputTokens = tokEl.GetInt32();
                    }
                    else if (eventType == "message_stop")
                    {
                        break;
                    }
                    else if (eventType == "error")
                    {
                        var errMsg = root.TryGetProperty("error", out var em) &&
                                     em.TryGetProperty("message", out var emm)
                            ? emm.GetString() : data;
                        AppLogger.Log("ClaudeChat", $"Stream error event: {errMsg}");
                        return (false, null, errMsg, 0);
                    }
                }
                catch { /* non-JSON or unrecognised event, skip */ }
            }

            AppLogger.Log("ClaudeChat", $"Stream complete — replyLen={reply.Length} outputTokens={outputTokens}");
            return (true, reply.ToString(), null, outputTokens);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("ClaudeChat: stream failed", ex);
            return (false, null, ex.Message, 0);
        }
    }

    // ── Agentic loop (orchestrator + sub-agents) ──────────────────

    /// <summary>
    /// Run a multi-turn agentic loop. The orchestrator streams text and may call
    /// tools; each tool call is executed by <paramref name="executeTool"/> and the
    /// result fed back in. Continues until stop_reason is "end_turn" or the
    /// safety iteration cap is reached.
    /// </summary>
    public async Task<(bool Success, string? FinalText, string? Error)> RunAgentLoopAsync(
        IReadOnlyList<ClaudeChatEntry> conversationHistory,
        string systemPrompt,
        string model,
        object[] toolDefinitions,
        Action<string> onTextChunk,
        Func<string, string, CancellationToken, Task<string>> executeTool, // (agentName, task) → result
        Action<string, string> onToolInvoke,   // (agentName, task) for UI notification
        CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, null, "Claude API key not configured.");

        // Build mutable message list from conversation history
        var apiMessages = new List<object>(
            conversationHistory.Select(e => (object)new { role = e.Role, content = e.Content }));

        const int maxIterations = 10;
        var finalText = new StringBuilder();

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var (roundText, toolUses, stopReason, error) =
                await StreamOneRoundAsync(apiMessages, systemPrompt, model, toolDefinitions, onTextChunk, ct);

            if (error is not null)
                return (false, null, error);

            finalText.Clear();
            finalText.Append(roundText);

            if (stopReason == "end_turn" || toolUses.Count == 0)
                break;

            // Add the assistant's turn (text + tool_use blocks) to history
            var assistantContent = new List<object>();
            if (roundText.Length > 0)
                assistantContent.Add(new { type = "text", text = roundText });
            foreach (var (id, name, input) in toolUses)
                assistantContent.Add(new { type = "tool_use", id, name, input });
            apiMessages.Add(new { role = "assistant", content = assistantContent.ToArray() });

            // Execute each tool and collect results
            var toolResultContent = new List<object>();
            foreach (var (id, name, input) in toolUses)
            {
                var agentName = input.TryGetProperty("agent_name", out var an) ? an.GetString() ?? name : name;
                var task      = input.TryGetProperty("task",        out var tk) ? tk.GetString() ?? "" : "";
                onToolInvoke(agentName, task);
                var result = await executeTool(agentName, task, ct);
                toolResultContent.Add(new { type = "tool_result", tool_use_id = id, content = result });
            }
            apiMessages.Add(new { role = "user", content = toolResultContent.ToArray() });
        }

        return (true, finalText.ToString(), null);
    }

    private record ToolCallAccumulator(string Id, string Name, StringBuilder InputJson);

    private async Task<(string Text, List<(string Id, string Name, System.Text.Json.JsonElement Input)> ToolUses, string StopReason, string? Error)>
        StreamOneRoundAsync(
            List<object> messages,
            string? systemPrompt,
            string model,
            object[] tools,
            Action<string> onTextChunk,
            CancellationToken ct)
    {
        var payloadDict = new Dictionary<string, object>
        {
            ["model"]      = model,
            ["max_tokens"] = 8192,
            ["stream"]     = true,
            ["messages"]   = messages
        };
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            payloadDict["system"] = systemPrompt;
        if (tools.Length > 0)
            payloadDict["tools"] = tools;

        var json = JsonSerializer.Serialize(payloadDict);
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            string errMsg;
            try
            {
                using var errDoc = JsonDocument.Parse(errBody);
                errMsg = errDoc.RootElement.TryGetProperty("error", out var e) &&
                         e.TryGetProperty("message", out var m)
                    ? m.GetString() ?? errBody
                    : errBody;
            }
            catch { errMsg = errBody; }
            return ("", new(), "end_turn", $"API error {(int)response.StatusCode}: {errMsg}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        var textSb = new StringBuilder();
        var toolAccumulators = new Dictionary<int, ToolCallAccumulator>();
        int currentBlockIndex = -1;
        string stopReason = "end_turn";
        string? currentEventType = null;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (line.StartsWith("event:"))
            {
                currentEventType = line["event:".Length..].Trim();
                continue;
            }
            if (!line.StartsWith("data:")) continue;
            var data = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(data)) continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                var eventType = root.TryGetProperty("type", out var typeEl)
                    ? typeEl.GetString() : currentEventType;

                switch (eventType)
                {
                    case "content_block_start":
                        currentBlockIndex = root.GetProperty("index").GetInt32();
                        var cb = root.GetProperty("content_block");
                        if (cb.GetProperty("type").GetString() == "tool_use")
                        {
                            var toolId   = cb.TryGetProperty("id",   out var idEl)   ? idEl.GetString()   ?? "" : "";
                            var toolName = cb.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                            toolAccumulators[currentBlockIndex] = new ToolCallAccumulator(toolId, toolName, new StringBuilder());
                        }
                        break;

                    case "content_block_delta":
                        var delta = root.GetProperty("delta");
                        var deltaType = delta.GetProperty("type").GetString();
                        if (deltaType == "text_delta" &&
                            delta.TryGetProperty("text", out var textEl))
                        {
                            var chunk = textEl.GetString() ?? "";
                            textSb.Append(chunk);
                            if (chunk.Length > 0) onTextChunk(chunk);
                        }
                        else if (deltaType == "input_json_delta" &&
                                 toolAccumulators.TryGetValue(currentBlockIndex, out var acc) &&
                                 delta.TryGetProperty("partial_json", out var pjEl))
                        {
                            acc.InputJson.Append(pjEl.GetString() ?? "");
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("delta", out var msgDelta) &&
                            msgDelta.TryGetProperty("stop_reason", out var sr))
                            stopReason = sr.GetString() ?? "end_turn";
                        break;

                    case "message_stop":
                        goto loopDone;
                }
            }
            catch { /* unrecognised event */ }
        }
        loopDone:

        // Parse accumulated tool_use inputs
        var toolUses = new List<(string, string, System.Text.Json.JsonElement)>();
        foreach (var acc in toolAccumulators.Values)
        {
            try
            {
                using var inputDoc = JsonDocument.Parse(
                    acc.InputJson.Length > 0 ? acc.InputJson.ToString() : "{}");
                toolUses.Add((acc.Id, acc.Name, inputDoc.RootElement.Clone()));
            }
            catch
            {
                toolUses.Add((acc.Id, acc.Name, JsonDocument.Parse("{}").RootElement.Clone()));
            }
        }

        AppLogger.Log("ClaudeAgent", $"Round complete — text={textSb.Length}ch tools={toolUses.Count} stopReason={stopReason}");
        return (textSb.ToString(), toolUses, stopReason, null);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

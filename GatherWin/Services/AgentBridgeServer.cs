using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace GatherWin.Services;

/// <summary>
/// Tiny local HTTP server that bridges GatherWin agent conversations
/// to an external Claude Code session (or the cc_agent.py runner).
///
/// Endpoints (all on http://localhost:{Port}/):
///   GET  /api/poll    — returns next pending message as JSON, or 204 No Content
///   POST /api/respond — Claude Code posts {"id":"...","content":"..."} to complete a pending message
///   GET  /api/status  — returns {"pending": N, "port": N}
/// </summary>
public class AgentBridgeServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<BridgeMessage> _queue = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public int Port { get; }
    public bool IsRunning { get; private set; }
    public int PendingCount => _queue.Count + _pending.Count;

    public AgentBridgeServer(int port = 7432)
    {
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            IsRunning = true;
            _ = RunAsync(_cts.Token);
            AppLogger.Log("AgentBridge", $"Bridge listening on http://localhost:{Port}/");
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"AgentBridge: failed to start on port {Port}", ex);
            IsRunning = false;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleRequestAsync(ctx, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { AppLogger.LogError("AgentBridge: listener loop failed", ex); }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;
        resp.Headers.Add("Access-Control-Allow-Origin", "*");

        try
        {
            var path   = req.Url?.AbsolutePath ?? "";
            var method = req.HttpMethod;

            if (method == "GET" && path == "/api/poll")
            {
                if (_queue.TryDequeue(out var msg))
                {
                    // Move from queue to pending-awaiting-response
                    // (if already removed from queue, TCS was already added in SendAndWaitAsync)
                    var json = JsonSerializer.Serialize(new
                    {
                        id            = msg.Id,
                        agent_name    = msg.AgentName,
                        system_prompt = msg.SystemPrompt,
                        history       = msg.History,
                        message       = msg.Message
                    });
                    resp.ContentType = "application/json; charset=utf-8";
                    resp.StatusCode  = 200;
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await resp.OutputStream.WriteAsync(bytes, ct);
                    AppLogger.Log("AgentBridge", $"Delivered message {msg.Id} to poller");
                }
                else
                {
                    resp.StatusCode = 204;
                }
            }
            else if (method == "POST" && path == "/api/respond")
            {
                using var sr   = new System.IO.StreamReader(req.InputStream, Encoding.UTF8);
                var body       = await sr.ReadToEndAsync(ct);
                using var doc  = JsonDocument.Parse(body);
                var id      = doc.RootElement.GetProperty("id").GetString()      ?? "";
                var content = doc.RootElement.GetProperty("content").GetString() ?? "";

                if (_pending.TryRemove(id, out var tcs))
                {
                    tcs.SetResult(content);
                    AppLogger.Log("AgentBridge", $"Response received for {id} ({content.Length} chars)");
                    resp.StatusCode = 200;
                }
                else
                {
                    AppLogger.Log("AgentBridge", $"Response for unknown id {id} (already timed out?)");
                    resp.StatusCode = 404;
                }
            }
            else if (method == "GET" && path == "/api/status")
            {
                var json  = JsonSerializer.Serialize(new { pending = PendingCount, port = Port });
                resp.ContentType = "application/json; charset=utf-8";
                resp.StatusCode  = 200;
                var bytes = Encoding.UTF8.GetBytes(json);
                await resp.OutputStream.WriteAsync(bytes, ct);
            }
            else
            {
                resp.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("AgentBridge: request handler error", ex);
            try { resp.StatusCode = 500; } catch { }
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    /// <summary>
    /// Enqueue a message and wait up to 5 minutes for Claude Code to respond.
    /// </summary>
    public async Task<string> SendAndWaitAsync(
        string agentName, string systemPrompt,
        string message, string history,
        CancellationToken ct)
    {
        var id  = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        _queue.Enqueue(new BridgeMessage(id, agentName, systemPrompt, message, history));
        AppLogger.Log("AgentBridge", $"Enqueued [{agentName}] id={id}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            return await tcs.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        IsRunning = false;
        // Cancel all pending waits
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
    }

    private record BridgeMessage(
        string Id, string AgentName, string SystemPrompt,
        string Message, string History);
}

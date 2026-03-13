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
///   GET  /api/poll              — returns next pending message for any agent
///   GET  /api/poll/{agentId}    — returns next pending message for that specific agent
///   POST /api/respond           — Claude Code posts {"id":"...","content":"..."} to complete a message
///   POST /api/heartbeat         — Claude Code posts {"id":"...","status":"..."} for in-progress updates
///   GET  /api/status            — returns {"pending": N, "port": N}
/// </summary>
public class AgentBridgeServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    // Per-agent queues keyed by agentId
    private readonly ConcurrentDictionary<string, ConcurrentQueue<BridgeMessage>> _queues = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    // Heartbeat callbacks: messageId → action to call with status text
    private readonly ConcurrentDictionary<string, Action<string>> _heartbeatCallbacks = new();

    public int Port { get; }
    public bool IsRunning { get; private set; }
    public int PendingCount => _queues.Values.Sum(q => q.Count) + _pending.Count;

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
            var path   = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
            var method = req.HttpMethod;

            // GET /api/poll[/{agentId}]
            if (method == "GET" && (path == "/api/poll" || path.StartsWith("/api/poll/")))
            {
                var agentId = path.Length > "/api/poll/".Length - 1
                    ? path["/api/poll/".Length..]
                    : "";

                var msg = TryDequeue(agentId);
                if (msg is not null)
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        id            = msg.Id,
                        agent_id      = msg.AgentId,
                        agent_name    = msg.AgentName,
                        system_prompt = msg.SystemPrompt,
                        history       = msg.History,
                        message       = msg.Message
                    });
                    resp.ContentType = "application/json; charset=utf-8";
                    resp.StatusCode  = 200;
                    await resp.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
                    AppLogger.Log("AgentBridge", $"Delivered {msg.Id} [{msg.AgentName}] to poller");
                }
                else
                {
                    resp.StatusCode = 204;
                }
            }
            // POST /api/respond
            else if (method == "POST" && path == "/api/respond")
            {
                var (id, content) = await ReadIdContentAsync(req, ct);
                if (_pending.TryRemove(id, out var tcs))
                {
                    _heartbeatCallbacks.TryRemove(id, out _);
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
            // POST /api/heartbeat  {"id":"...","status":"..."}
            else if (method == "POST" && path == "/api/heartbeat")
            {
                var (id, status) = await ReadIdContentAsync(req, ct, contentKey: "status");
                if (_heartbeatCallbacks.TryGetValue(id, out var callback))
                {
                    callback(status);
                    AppLogger.Log("AgentBridge", $"Heartbeat for {id}: {status}");
                }
                resp.StatusCode = 200;
            }
            // GET /api/status
            else if (method == "GET" && path == "/api/status")
            {
                var json = JsonSerializer.Serialize(new { pending = PendingCount, port = Port });
                resp.ContentType = "application/json; charset=utf-8";
                resp.StatusCode  = 200;
                await resp.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
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

    private static async Task<(string Id, string Content)> ReadIdContentAsync(
        HttpListenerRequest req, CancellationToken ct, string contentKey = "content")
    {
        using var sr  = new System.IO.StreamReader(req.InputStream, Encoding.UTF8);
        var body      = await sr.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var id      = doc.RootElement.TryGetProperty("id",       out var idEl)  ? idEl.GetString()  ?? "" : "";
        var content = doc.RootElement.TryGetProperty(contentKey, out var conEl) ? conEl.GetString() ?? "" : "";
        return (id, content);
    }

    private BridgeMessage? TryDequeue(string agentId)
    {
        if (!string.IsNullOrEmpty(agentId))
        {
            if (_queues.TryGetValue(agentId, out var q) && q.TryDequeue(out var m))
                return m;
            return null;
        }
        foreach (var queue in _queues.Values)
        {
            if (queue.TryDequeue(out var m))
                return m;
        }
        return null;
    }

    /// <summary>
    /// Enqueue a message and wait up to 20 minutes for Claude Code to respond.
    /// <paramref name="onHeartbeat"/> is called whenever the agent posts a heartbeat update.
    /// </summary>
    public async Task<string> SendAndWaitAsync(
        string agentId, string agentName, string systemPrompt,
        string message, string history,
        Action<string>? onHeartbeat,
        CancellationToken ct)
    {
        var id  = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        if (onHeartbeat is not null)
            _heartbeatCallbacks[id] = onHeartbeat;

        var queue = _queues.GetOrAdd(agentId, _ => new ConcurrentQueue<BridgeMessage>());
        queue.Enqueue(new BridgeMessage(id, agentId, agentName, systemPrompt, message, history));
        AppLogger.Log("AgentBridge", $"Enqueued [{agentName}] id={id}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));

        try
        {
            return await tcs.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            _heartbeatCallbacks.TryRemove(id, out _);
            throw;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        IsRunning = false;
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
        _heartbeatCallbacks.Clear();
    }

    private record BridgeMessage(
        string Id, string AgentId, string AgentName,
        string SystemPrompt, string Message, string History);
}

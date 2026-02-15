using GatherWin.Models;

namespace GatherWin.Services;

/// <summary>
/// Background polling service that monitors Gather for new activity.
/// Raises events instead of console output — ViewModels subscribe and update the UI.
/// </summary>
public class PollingService
{
    private readonly GatherApiClient _api;
    private readonly string _agentId;

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private TimeSpan _pollInterval;
    private string[] _watchedPostIds;

    // State tracking (same as GatherPing's MonitorService)
    private readonly Dictionary<string, HashSet<string>> _seenCommentIds = new();
    private readonly HashSet<string> _seenInboxMessageIds = new();
    private readonly HashSet<string> _seenFeedPostIds = new();
    private readonly Dictionary<string, HashSet<string>> _seenChannelMessageIds = new();
    private readonly Dictionary<string, string> _channelNames = new();
    private readonly HashSet<string> _knownChannelIds = new();
    private DateTimeOffset _feedSinceTimestamp;
    // Channel polling does not use a `since` timestamp — see CheckChannelsAsync for details.
    /// <summary>
    /// Number of poll cycles remaining that should be treated as "initial load" (seeding).
    /// The Gather API can return subtly different data between consecutive polls, so we
    /// seed for 2 cycles to capture any variance before reporting genuinely new items.
    /// </summary>
    private int _seedPollsRemaining = 2;

    // Events
    public event EventHandler<CommentEventArgs>? NewCommentReceived;
    public event EventHandler<InboxMessageEventArgs>? NewInboxMessageReceived;
    public event EventHandler<FeedPostEventArgs>? NewFeedPostReceived;
    public event EventHandler<ChannelMessageEventArgs>? NewChannelMessageReceived;
    public event EventHandler<NewChannelDiscoveredEventArgs>? NewChannelDiscovered;
    public event EventHandler<InitialStateEventArgs>? InitialStateLoaded;
    public event EventHandler<DateTimeOffset>? PollCycleCompleted;
    public event EventHandler<string>? PollError;

    public bool IsRunning => _pollingTask is not null && !_pollingTask.IsCompleted;

    /// <summary>When true, the first poll skips the channel fetch (already loaded by ChannelsViewModel).</summary>
    public bool SkipInitialChannelFetch { get; set; }

    /// <summary>When true, the first poll skips the feed fetch (already pre-loaded by MainViewModel).</summary>
    public bool SkipInitialFeedFetch { get; set; }

    /// <summary>Seed the seen-feed-post-IDs set so pre-loaded posts aren't duplicated.</summary>
    public void SeedFeedPostIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            _seenFeedPostIds.Add(id);
    }

    /// <summary>Seed the known channel IDs so pre-loaded channels aren't reported as new.</summary>
    public void SeedChannelIds(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            _knownChannelIds.Add(id);
    }

    /// <summary>Seed the seen-channel-message-IDs so pre-loaded messages aren't duplicated.</summary>
    public void SeedChannelMessageIds(string channelId, IEnumerable<string> ids)
    {
        if (!_seenChannelMessageIds.TryGetValue(channelId, out var set))
        {
            set = new HashSet<string>();
            _seenChannelMessageIds[channelId] = set;
        }
        foreach (var id in ids)
            set.Add(id);
    }

    public PollingService(GatherApiClient api, string agentId, string[] watchedPostIds, int intervalSeconds)
    {
        _api = api;
        _agentId = agentId;
        _watchedPostIds = watchedPostIds;
        _pollInterval = TimeSpan.FromSeconds(intervalSeconds);
        _feedSinceTimestamp = DateTimeOffset.UtcNow.AddDays(-7);
        // No channel since timestamp — channels fetch all recent messages each poll.
    }

    public void UpdateSettings(string[] watchedPostIds, int intervalSeconds)
    {
        _watchedPostIds = watchedPostIds;
        _pollInterval = TimeSpan.FromSeconds(intervalSeconds);
    }

    public async Task StartAsync(CancellationToken externalCt)
    {
        AppLogger.Log("Poll", $"Starting polling (interval={_pollInterval.TotalSeconds}s, posts={string.Join(",", _watchedPostIds)})");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _timer = new PeriodicTimer(_pollInterval);

        // Do initial poll immediately
        AppLogger.Log("Poll", "Running initial poll...");
        await PollOnceAsync(_cts.Token);

        // Start background polling
        AppLogger.Log("Poll", "Initial poll complete, starting background loop");
        _pollingTask = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        AppLogger.Log("Poll", "Stopping polling service");
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Dispose();
        _timer = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                try
                {
                    await PollOnceAsync(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLogger.LogError("Poll: cycle failed", ex);
                    PollError?.Invoke(this, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }
    }

    private bool IsSeeding => _seedPollsRemaining > 0;

    private async Task PollOnceAsync(CancellationToken ct)
    {
        AppLogger.Log("Poll", IsSeeding ? $"Seed poll ({_seedPollsRemaining} remaining)..." : "Poll cycle starting...");

        await CheckPostCommentsAsync(ct);
        await CheckInboxAsync(ct);
        await CheckFeedAsync(ct);
        await CheckChannelsAsync(ct);

        if (IsSeeding)
        {
            _seedPollsRemaining--;
            if (_seedPollsRemaining == 0)
            {
                AppLogger.Log("Poll", "Initial state seeded (2 cycles)");
                InitialStateLoaded?.Invoke(this, new InitialStateEventArgs());
            }
        }

        AppLogger.Log("Poll", "Poll cycle completed");
        PollCycleCompleted?.Invoke(this, DateTimeOffset.Now);
    }

    // ── Comment Monitoring ──────────────────────────────────────

    private async Task CheckPostCommentsAsync(CancellationToken ct)
    {
        foreach (var postId in _watchedPostIds)
        {
            var post = await _api.GetPostWithCommentsAsync(postId, ct);
            if (post is null) continue;

            if (!_seenCommentIds.TryGetValue(postId, out var seenIds))
            {
                seenIds = new HashSet<string>();
                _seenCommentIds[postId] = seenIds;
            }

            var comments = post.Comments ?? [];

            if (IsSeeding)
            {
                // Seed using content fingerprints — the API may return unstable IDs
                var isFirstSeed = _seedPollsRemaining == 2;
                AppLogger.Log("Poll", $"Seeding {comments.Count} comments from \"{post.Title}\"");
                foreach (var c in comments)
                {
                    seenIds.Add(CommentFingerprint(c));

                    // Only populate the UI on the very first seed poll
                    if (isFirstSeed)
                    {
                        NewCommentReceived?.Invoke(this, new CommentEventArgs
                        {
                            PostId = postId,
                            PostTitle = post.Title ?? postId,
                            CommentId = c.Id ?? "",
                            Author = c.Author ?? "unknown",
                            Body = c.Body ?? "(empty)",
                            Timestamp = ParseTimestamp(c.Created),
                            IsInitialLoad = true
                        });
                    }
                }
                continue;
            }

            foreach (var comment in comments)
            {
                if (!seenIds.Add(CommentFingerprint(comment))) continue;
                if (comment.AuthorId == _agentId) continue;

                AppLogger.Log("Poll", $"New comment on \"{post.Title}\" by {comment.Author}: {(comment.Body ?? "")[..Math.Min(50, (comment.Body ?? "").Length)]}...");
                NewCommentReceived?.Invoke(this, new CommentEventArgs
                {
                    PostId = postId,
                    PostTitle = post.Title ?? postId,
                    CommentId = comment.Id ?? "",
                    Author = comment.Author ?? "unknown",
                    Body = comment.Body ?? "(empty)",
                    Timestamp = ParseTimestamp(comment.Created)
                });
            }
        }
    }

    /// <summary>
    /// Generate a stable fingerprint for a comment based on its content,
    /// since the Gather API may return unstable IDs across polls.
    /// </summary>
    private static string CommentFingerprint(GatherComment c) =>
        $"{c.Author}|{c.AuthorId}|{c.Body}|{c.Created}";

    // ── Inbox Monitoring ────────────────────────────────────────

    private async Task CheckInboxAsync(CancellationToken ct)
    {
        var inbox = await _api.GetInboxAsync(ct);
        if (inbox is null) return;

        var messages = inbox.Messages ?? [];

        if (IsSeeding)
        {
            var isFirstSeed = _seedPollsRemaining == 2;
            AppLogger.Log("Poll", $"Seeding {messages.Count} inbox messages");
            foreach (var m in messages)
            {
                // Use content fingerprint — the API may return unstable/null IDs
                _seenInboxMessageIds.Add(InboxFingerprint(m));

                // Only populate the UI on the very first seed poll
                if (isFirstSeed)
                {
                    NewInboxMessageReceived?.Invoke(this, new InboxMessageEventArgs
                    {
                        MessageId = m.Id ?? "",
                        Subject = m.Subject ?? m.Type ?? "message",
                        Body = m.Body ?? "(empty)",
                        Timestamp = ParseTimestamp(m.Created),
                        IsInitialLoad = true,
                        PostId = m.PostId,
                        CommentId = m.CommentId,
                        ChannelId = m.ChannelId
                    });
                }
            }
            return;
        }

        foreach (var message in messages)
        {
            if (!_seenInboxMessageIds.Add(InboxFingerprint(message))) continue;

            AppLogger.Log("Poll", $"New inbox message: {message.Subject ?? message.Type ?? "message"}");
            NewInboxMessageReceived?.Invoke(this, new InboxMessageEventArgs
            {
                MessageId = message.Id ?? "",
                Subject = message.Subject ?? message.Type ?? "message",
                Body = message.Body ?? "(empty)",
                Timestamp = ParseTimestamp(message.Created),
                PostId = message.PostId,
                CommentId = message.CommentId,
                ChannelId = message.ChannelId
            });
        }
    }

    /// <summary>
    /// Generate a stable fingerprint for an inbox message based on its content,
    /// since the Gather API may return unstable or null notification IDs.
    /// </summary>
    private static string InboxFingerprint(InboxMessage m) =>
        $"{m.Subject}|{m.Body}|{m.Created}|{m.PostId}|{m.CommentId}|{m.ChannelId}";

    // ── Feed Monitoring ─────────────────────────────────────────

    private async Task CheckFeedAsync(CancellationToken ct)
    {
        var feed = await _api.GetFeedPostsAsync(_feedSinceTimestamp, ct);
        if (feed is null) return;

        var posts = feed.Posts ?? [];

        if (IsSeeding)
        {
            if (SkipInitialFeedFetch)
            {
                // Feed was pre-loaded by MainViewModel — just seed IDs we don't already have
                AppLogger.Log("Poll", $"Skipping feed seed (pre-loaded), syncing {posts.Count} IDs");
                foreach (var p in posts)
                    if (p.Id is not null) _seenFeedPostIds.Add(p.Id);
            }
            else
            {
                var isFirstSeed = _seedPollsRemaining == 2;
                AppLogger.Log("Poll", $"Seeding {posts.Count} feed posts");
                foreach (var p in posts)
                {
                    if (p.Id is not null) _seenFeedPostIds.Add(p.Id);

                    // Only populate the UI on the very first seed poll
                    if (isFirstSeed)
                    {
                        NewFeedPostReceived?.Invoke(this, new FeedPostEventArgs
                        {
                            PostId = p.Id ?? "",
                            Author = p.Author ?? "unknown",
                            Title = p.Title ?? p.Summary ?? "(no title)",
                            Body = p.Body ?? p.Summary ?? "",
                            Timestamp = ParseTimestamp(p.Created),
                            IsInitialLoad = true
                        });
                    }
                }
            }
            _feedSinceTimestamp = DateTimeOffset.UtcNow;
            return;
        }

        foreach (var post in posts)
        {
            if (post.Id is null) continue;
            if (!_seenFeedPostIds.Add(post.Id)) continue;
            if (post.AuthorId == _agentId) continue;

            AppLogger.Log("Poll", $"New feed post by {post.Author}: {post.Title ?? post.Summary ?? "(no title)"}");
            NewFeedPostReceived?.Invoke(this, new FeedPostEventArgs
            {
                PostId = post.Id,
                Author = post.Author ?? "unknown",
                Title = post.Title ?? post.Summary ?? "(no title)",
                Body = post.Body ?? post.Summary ?? "",
                Timestamp = ParseTimestamp(post.Created)
            });
        }

        if (_seenFeedPostIds.Count > 10000)
        {
            _seenFeedPostIds.Clear();
            foreach (var p in posts)
                if (p.Id is not null) _seenFeedPostIds.Add(p.Id);
        }

        _feedSinceTimestamp = DateTimeOffset.UtcNow;
    }

    // ── Channel Monitoring ──────────────────────────────────────

    private async Task CheckChannelsAsync(CancellationToken ct)
    {
        if (IsSeeding && SkipInitialChannelFetch)
        {
            AppLogger.Log("Poll", "Skipping initial channel fetch (pre-loaded by ChannelsViewModel)");
            return;
        }

        var channelList = await _api.GetChannelsAsync(ct);
        if (channelList is null) return;

        var channels = channelList.Channels ?? [];

        foreach (var channel in channels)
        {
            if (channel.Id is null) continue;

            if (channel.Name is not null)
                _channelNames[channel.Id] = channel.Name;

            var channelName = _channelNames.GetValueOrDefault(channel.Id, channel.Id);

            // Detect newly appeared channels (created externally or by other agents)
            if (!IsSeeding && _knownChannelIds.Add(channel.Id))
            {
                AppLogger.Log("Poll", $"New channel discovered: #{channelName} ({channel.Id})");
                NewChannelDiscovered?.Invoke(this, new NewChannelDiscoveredEventArgs
                {
                    ChannelId = channel.Id,
                    ChannelName = channelName,
                    Description = channel.Description ?? "",
                    MemberCount = channel.MemberCount
                });
            }
            else if (IsSeeding)
            {
                _knownChannelIds.Add(channel.Id);
            }

            if (!_seenChannelMessageIds.TryGetValue(channel.Id, out var seenIds))
            {
                seenIds = new HashSet<string>();
                _seenChannelMessageIds[channel.Id] = seenIds;
            }

            // Fetch without `since` filter — the Gather API's since parameter is unreliable
            // for channel messages (newly created messages don't appear in filtered queries).
            // We rely on seenIds for deduplication instead.
            var msgResp = await _api.GetChannelMessagesAsync(channel.Id, null, ct);
            if (msgResp is null) continue;

            var messages = msgResp.Messages ?? [];

            if (IsSeeding)
            {
                var isFirstSeed = _seedPollsRemaining == 2;
                AppLogger.Log("Poll", $"Seeding {messages.Count} messages from #{channelName}");
                foreach (var m in messages)
                {
                    if (m.Id is not null) seenIds.Add(m.Id);

                    // Only populate the UI on the very first seed poll
                    if (isFirstSeed)
                    {
                        NewChannelMessageReceived?.Invoke(this, new ChannelMessageEventArgs
                        {
                            ChannelId = channel.Id,
                            ChannelName = channelName,
                            MessageId = m.Id ?? "",
                            Author = m.AuthorName ?? m.AuthorId ?? "unknown",
                            Body = m.Body ?? "(empty)",
                            Timestamp = ParseTimestamp(m.Created),
                            IsInitialLoad = true,
                            ReplyTo = m.ReplyTo
                        });
                    }
                }
                continue;
            }

            AppLogger.Log("Poll", $"Channel #{channelName}: API returned {messages.Count} msgs, seenIds has {seenIds.Count}");

            foreach (var msg in messages)
            {
                if (msg.Id is null) { AppLogger.Log("Poll", $"  msg: null ID, skipping"); continue; }
                if (!seenIds.Add(msg.Id)) { AppLogger.Log("Poll", $"  msg {msg.Id}: already seen"); continue; }

                AppLogger.Log("Poll", $"NEW channel msg in #{channelName} by {msg.AuthorName ?? msg.AuthorId}: {(msg.Body ?? "")[..Math.Min(50, (msg.Body ?? "").Length)]}...");
                NewChannelMessageReceived?.Invoke(this, new ChannelMessageEventArgs
                {
                    ChannelId = channel.Id,
                    ChannelName = channelName,
                    MessageId = msg.Id,
                    Author = msg.AuthorName ?? msg.AuthorId ?? "unknown",
                    Body = msg.Body ?? "(empty)",
                    Timestamp = ParseTimestamp(msg.Created),
                    ReplyTo = msg.ReplyTo
                });
            }

            if (seenIds.Count > 5000)
            {
                seenIds.Clear();
                foreach (var m in messages)
                    if (m.Id is not null) seenIds.Add(m.Id);
            }
        }

        // No timestamp tracking needed — we fetch all recent messages each cycle
        // and rely on seenIds for dedup (the Gather API's `since` filter is unreliable).
    }

    private static DateTimeOffset ParseTimestamp(string? ts)
    {
        if (ts is not null && DateTimeOffset.TryParse(ts, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }
}

// ── Event Args ──────────────────────────────────────────────────

public class CommentEventArgs : EventArgs
{
    public required string PostId { get; init; }
    public required string PostTitle { get; init; }
    public required string CommentId { get; init; }
    public required string Author { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public bool IsInitialLoad { get; init; }
}

public class InboxMessageEventArgs : EventArgs
{
    public required string MessageId { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public bool IsInitialLoad { get; init; }

    /// <summary>Post ID this notification references (if applicable).</summary>
    public string? PostId { get; init; }
    /// <summary>Comment ID this notification references (if applicable).</summary>
    public string? CommentId { get; init; }
    /// <summary>Channel ID this notification references (if applicable).</summary>
    public string? ChannelId { get; init; }
}

public class FeedPostEventArgs : EventArgs
{
    public required string PostId { get; init; }
    public required string Author { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public bool IsInitialLoad { get; init; }
}

public class ChannelMessageEventArgs : EventArgs
{
    public required string ChannelId { get; init; }
    public required string ChannelName { get; init; }
    public required string MessageId { get; init; }
    public required string Author { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public bool IsInitialLoad { get; init; }
    public string? ReplyTo { get; init; }
}

public class InitialStateEventArgs : EventArgs
{
    public string? PostTitle { get; init; }
    public string? PostId { get; init; }
    public int CommentCount { get; init; }
    public int Score { get; init; }
    public bool Verified { get; init; }
}

public class NewChannelDiscoveredEventArgs : EventArgs
{
    public required string ChannelId { get; init; }
    public required string ChannelName { get; init; }
    public required string Description { get; init; }
    public int MemberCount { get; init; }
}

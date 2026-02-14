using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;
using GatherWin.Views;

namespace GatherWin.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GatherApiClient _api;
    private readonly GatherAuthService _auth;
    private PollingService? _polling;
    private CancellationTokenSource? _pollCts;

    private readonly string _agentId;
    private string[] _watchedPostIds;
    private int _pollIntervalSeconds;
    private readonly string _keysDirectory;
    private string _claudeApiKey;
    private int _newBadgeDurationMinutes;

    // Badge expiry timer
    private readonly DispatcherTimer _badgeExpiryTimer;

    // Stored event handlers for cleanup
    private EventHandler<CommentEventArgs>? _onComment;
    private EventHandler<InboxMessageEventArgs>? _onInbox;
    private EventHandler<FeedPostEventArgs>? _onFeed;
    private EventHandler<ChannelMessageEventArgs>? _onChannel;
    private EventHandler<DateTimeOffset>? _onPollCycle;
    private EventHandler<string>? _onPollError;
    private EventHandler<InitialStateEventArgs>? _onInitialState;
    private readonly EventHandler<DateTimeOffset> _onTokenRefreshed;

    // Agent identity cache (Feature 10)
    [ObservableProperty] private string _currentAgentName = string.Empty;
    [ObservableProperty] private string _currentAgentDescription = string.Empty;

    // Agent lookup cache (Feature 9)
    private readonly Dictionary<string, AgentItem> _agentCache = new();

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _statusMessage = "Disconnected";
    [ObservableProperty] private string _connectionError = string.Empty;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private DateTimeOffset? _lastPollTime;
    [ObservableProperty] private string _tokenExpiryDisplay = string.Empty;

    /// <summary>Font scale factor (1.0 = 100%). Bound to the root LayoutTransform.</summary>
    [ObservableProperty] private double _fontScale = 1.0;

    public AccountViewModel Account { get; }
    public CommentsViewModel Comments { get; }
    public InboxViewModel Inbox { get; }
    public FeedViewModel Feed { get; }
    public ChannelsViewModel Channels { get; }
    public WhatsNewViewModel WhatsNew { get; }
    public AgentsViewModel Agents { get; }
    public ObservableCollection<ActivityItem> AllActivity { get; } = new();

    public string AgentId => _agentId;
    public string ClaudeApiKey => _claudeApiKey;
    public int NewBadgeDurationMinutes => _newBadgeDurationMinutes;

    /// <summary>Raised when new activity arrives and the window should flash.</summary>
    public event EventHandler? NewActivityArrived;

    public MainViewModel(
        GatherApiClient api,
        GatherAuthService auth,
        string agentId,
        string[] watchedPostIds,
        int pollIntervalSeconds,
        string keysDirectory,
        string claudeApiKey = "",
        int newBadgeDurationMinutes = 30)
    {
        _api = api;
        _auth = auth;
        _agentId = agentId;
        _watchedPostIds = watchedPostIds;
        _pollIntervalSeconds = pollIntervalSeconds;
        _keysDirectory = keysDirectory;
        _claudeApiKey = claudeApiKey;
        _newBadgeDurationMinutes = newBadgeDurationMinutes;

        Account = new AccountViewModel();
        Comments = new CommentsViewModel(api);
        Inbox = new InboxViewModel();
        Feed = new FeedViewModel(api);
        Channels = new ChannelsViewModel(api);
        WhatsNew = new WhatsNewViewModel(api, keysDirectory);
        Agents = new AgentsViewModel(api);

        // Apply saved post display preference
        _api.ShowFullPosts = WhatsNew.Options.ShowFullPosts;

        // Wire subscribe/unsubscribe callbacks
        Feed.SubscribeRequested = postId => _ = SubscribeToPostAsync(postId);
        Comments.UnsubscribeRequested = postId => UnsubscribeFromPost(postId);

        // Apply saved channel settings
        Channels.MaxChannels = WhatsNew.Options.MaxChannelsTab;
        Channels.ShowAllChannels = WhatsNew.Options.ShowAllChannels;
        Channels.SetSubscribedChannelIds(new HashSet<string>(WhatsNew.Options.SubscribedChannelIds));
        Channels.SubscriptionChanged = () =>
        {
            WhatsNew.Options.SubscribedChannelIds = Channels.SubscribedChannelIds.ToList();
            WhatsNew.Options.ShowAllChannels = Channels.ShowAllChannels;
            WhatsNew.SaveOptions();
        };

        // Apply saved agents settings
        Agents.MaxAgents = WhatsNew.Options.MaxAgentsTab;
        Agents.PostCreated = postId => _ = SubscribeToPostAsync(postId);
        Agents.NavigateToPost = postId => _ = NavigateToDiscussionsTabAsync(postId);

        // Apply saved font scale
        FontScale = WhatsNew.Options.FontScalePercent / 100.0;

        // Badge expiry timer — ticks every 30 seconds
        _badgeExpiryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _badgeExpiryTimer.Tick += BadgeExpiryTimer_Tick;
        _badgeExpiryTimer.Start();

        // Watch for token refreshes
        _onTokenRefreshed = (_, expiry) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Account.TokenExpiry = expiry;
                TokenExpiryDisplay = $"Token valid until {expiry.ToLocalTime():HH:mm:ss}";
            });
        };
        _auth.TokenRefreshed += _onTokenRefreshed;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // Reset new-item count when user switches to a tab
        switch (value)
        {
            case 1: Comments.ResetNewCount(); break;
            case 2: Inbox.ResetNewCount(); break;
            case 3: Feed.ResetNewCount(); break;
            case 4: Channels.ResetNewCount(); break;
            case 5:
                // Auto-load agents when switching to Agents tab (only if not already loaded)
                if (IsConnected && !Agents.IsLoading && Agents.Agents.Count == 0)
                    _ = Agents.LoadAgentsAsync(CancellationToken.None);
                break;
            case 6:
                WhatsNew.ResetNewCount();
                // Auto-check when switching to What's New tab (if connected)
                if (IsConnected && !WhatsNew.IsChecking)
                    _ = WhatsNew.CheckForNewsAsync(CancellationToken.None);
                break;
        }
    }

    // ── Badge Expiry Timer ─────────────────────────────────────

    private void BadgeExpiryTimer_Tick(object? sender, EventArgs e)
    {
        if (_newBadgeDurationMinutes <= 0) return;

        var cutoff = DateTimeOffset.Now.AddMinutes(-_newBadgeDurationMinutes);

        ExpireBadges(AllActivity, cutoff);
        ExpireBadges(Comments.Comments, cutoff);
        ExpireBadges(Inbox.Messages, cutoff);
        ExpireBadges(Feed.Posts, cutoff);
        ExpireWhatsNewBadges(cutoff);
        ExpireChannelBadges(cutoff);
    }

    private void ExpireBadges(ObservableCollection<ActivityItem> items, DateTimeOffset cutoff)
    {
        foreach (var item in items)
        {
            if (item.IsNew && item.MarkedNewAt != default && item.MarkedNewAt < cutoff)
            {
                item.IsNew = false;
            }
        }
    }

    private void ExpireWhatsNewBadges(DateTimeOffset cutoff)
    {
        foreach (var entry in WhatsNew.Entries)
        {
            if (entry.IsNew && entry.MarkedNewAt != default && entry.MarkedNewAt < cutoff)
            {
                entry.IsNew = false;
            }
        }
    }

    private void ExpireChannelBadges(DateTimeOffset cutoff)
    {
        foreach (var channel in Channels.Channels)
        {
            foreach (var msg in channel.Messages)
            {
                if (msg.IsNew && msg.MarkedNewAt != default && msg.MarkedNewAt < cutoff)
                {
                    msg.IsNew = false;
                    channel.NewMessageCount = Math.Max(0, channel.NewMessageCount - 1);
                }
            }
        }
    }

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken ct)
    {
        if (IsConnected)
        {
            Disconnect();
            return;
        }

        IsConnecting = true;
        ConnectionError = string.Empty;
        StatusMessage = "Connecting...";

        AppLogger.Log("VM", "Connect button clicked, starting connection...");

        try
        {
            // Authenticate
            AppLogger.Log("VM", "Authenticating...");
            await _auth.EnsureAuthenticatedAsync(ct);

            Account.IsAuthenticated = true;
            Account.TokenExpiry = _auth.TokenExpiry;
            TokenExpiryDisplay = $"Token valid until {_auth.TokenExpiry.ToLocalTime():HH:mm:ss}";
            AppLogger.Log("VM", "Authenticated OK");

            // Fetch own agent info (Feature 10)
            try
            {
                var agentInfo = await _api.GetAgentByIdAsync(_agentId, ct);
                if (agentInfo is not null)
                {
                    CurrentAgentName = agentInfo.Name ?? _agentId;
                    CurrentAgentDescription = agentInfo.Description ?? "";
                    AppLogger.Log("VM", $"Agent identity: {CurrentAgentName}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("VM: Failed to fetch agent info", ex);
            }

            // Fetch balance
            AppLogger.Log("VM", "Fetching balance...");
            var balance = await _api.GetBalanceAsync(ct);
            if (balance is not null)
            {
                AppLogger.Log("VM", $"Balance: {balance.BalanceBch} BCH (~${balance.BalanceUsdApprox})");
                Application.Current.Dispatcher.Invoke(() => Account.UpdateFromBalance(balance));
            }
            else
            {
                AppLogger.Log("VM", "Balance response was null");
            }

            // Fetch watched post stats and populate Discussions list
            AppLogger.Log("VM", $"Loading stats for {_watchedPostIds.Length} watched posts...");
            await LoadWatchedPostStatsAsync(ct);

            // Load all channels for the Channels tab
            AppLogger.Log("VM", "Loading all channels...");
            await Channels.LoadAllChannelsAsync(ct);

            // Start polling
            AppLogger.Log("VM", "Starting polling service...");
            _pollCts = new CancellationTokenSource();
            _polling = new PollingService(_api, _agentId, _watchedPostIds, _pollIntervalSeconds);
            WirePollingEvents(_polling);
            await _polling.StartAsync(_pollCts.Token);

            IsConnected = true;
            StatusMessage = $"Connected — polling every {_pollIntervalSeconds}s";
            AppLogger.Log("VM", "Connection complete, polling started");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("VM: Connection failed", ex);
            ConnectionError = ex.Message;
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void Disconnect()
    {
        AppLogger.Log("VM", "Disconnecting...");
        UnwirePollingEvents();
        _polling?.Stop();
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _polling = null;
        _pollCts = null;

        IsConnected = false;
        Account.IsAuthenticated = false;
        StatusMessage = "Disconnected";
        AppLogger.Log("VM", "Disconnected");
    }

    public void Shutdown()
    {
        _badgeExpiryTimer.Tick -= BadgeExpiryTimer_Tick;
        _badgeExpiryTimer.Stop();
        _auth.TokenRefreshed -= _onTokenRefreshed;
        Disconnect();
    }

    private async Task LoadWatchedPostStatsAsync(CancellationToken ct)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Account.WatchedPosts.Clear();
            Comments.Discussions.Clear();
        });

        foreach (var postId in _watchedPostIds)
        {
            var post = await _api.GetPostWithCommentsAsync(postId, ct);
            if (post is null) continue;

            var info = new WatchedPostInfo
            {
                PostId = postId,
                Title = post.Title ?? post.Summary ?? "(untitled)",
                Score = post.Score,
                CommentCount = post.CommentCount,
                IsVerified = post.Verified
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                Account.WatchedPosts.Add(info);

                // Also populate the Discussions list (Feature 1)
                Comments.Discussions.Add(new WatchedDiscussionItem
                {
                    PostId = postId,
                    Title = post.Title ?? post.Summary ?? "(untitled)",
                    Author = post.Author ?? "unknown",
                    CommentCount = post.CommentCount,
                    LastActivity = DateTimeOffset.Now,
                    NewCommentCount = 0
                });
            });
        }
    }

    private void WirePollingEvents(PollingService polling)
    {
        _onComment = (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            var now = DateTimeOffset.Now;
            Comments.AddComment(e.PostId, e.PostTitle, e.Author, e.Body, e.Timestamp, isNew);

            // Update discussion list comment counts
            if (isNew)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var disc = Comments.Discussions.FirstOrDefault(d => d.PostId == e.PostId);
                    if (disc is not null)
                    {
                        disc.CommentCount++;
                        disc.NewCommentCount++;
                        disc.LastActivity = now;
                    }
                });
            }

            AddToAllActivity(new ActivityItem
            {
                Type = ActivityType.Comment,
                Id = e.CommentId,
                Title = e.PostTitle,
                Author = e.Author,
                Body = e.Body,
                Timestamp = e.Timestamp,
                PostId = e.PostId,
                IsNew = isNew,
                MarkedNewAt = isNew ? now : default
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        _onInbox = (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            var now = DateTimeOffset.Now;
            Inbox.AddMessage(e.Subject, e.Body, e.Timestamp, isNew, e.PostId, e.CommentId, e.ChannelId);
            AddToAllActivity(new ActivityItem
            {
                Type = ActivityType.Inbox,
                Id = e.MessageId,
                Title = e.Subject,
                Author = string.Empty,
                Body = e.Body,
                Timestamp = e.Timestamp,
                PostId = e.PostId,
                CommentId = e.CommentId,
                ChannelId = e.ChannelId,
                IsNew = isNew,
                MarkedNewAt = isNew ? now : default
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        _onFeed = (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            var now = DateTimeOffset.Now;
            Feed.AddPost(e.PostId, e.Author, e.Title, e.Body, e.Timestamp, isNew);
            AddToAllActivity(new ActivityItem
            {
                Type = ActivityType.FeedPost,
                Id = e.PostId,
                Title = e.Title,
                Author = e.Author,
                Body = e.Body,
                Timestamp = e.Timestamp,
                IsNew = isNew,
                MarkedNewAt = isNew ? now : default
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        _onChannel = (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            var now = DateTimeOffset.Now;
            Channels.AddMessage(e.ChannelId, e.ChannelName, e.MessageId, e.Author, e.Body,
                e.Timestamp, isNew, e.ReplyTo);
            AddToAllActivity(new ActivityItem
            {
                Type = ActivityType.Channel,
                Id = e.MessageId,
                Title = e.ChannelName,
                Author = e.Author,
                Body = e.Body,
                Timestamp = e.Timestamp,
                ChannelId = e.ChannelId,
                ChannelName = e.ChannelName,
                IsNew = isNew,
                MarkedNewAt = isNew ? now : default
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        _onPollCycle = (_, time) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LastPollTime = time;
                StatusMessage = $"Connected — polling every {_pollIntervalSeconds}s | Last: {time.ToLocalTime():HH:mm:ss}";
            });
        };

        _onPollError = (_, error) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Poll error: {error}";
            });
        };

        _onInitialState = (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Connected — polling every {_pollIntervalSeconds}s | Initial state loaded";

                // Auto-select first channel so messages are visible
                if (Channels.SelectedChannel is null && Channels.Channels.Count > 0)
                    Channels.SelectedChannel = Channels.Channels[0];
            });
        };

        polling.NewCommentReceived += _onComment;
        polling.NewInboxMessageReceived += _onInbox;
        polling.NewFeedPostReceived += _onFeed;
        polling.NewChannelMessageReceived += _onChannel;
        polling.PollCycleCompleted += _onPollCycle;
        polling.PollError += _onPollError;
        polling.InitialStateLoaded += _onInitialState;
    }

    private void UnwirePollingEvents()
    {
        if (_polling is null) return;

        if (_onComment is not null) _polling.NewCommentReceived -= _onComment;
        if (_onInbox is not null) _polling.NewInboxMessageReceived -= _onInbox;
        if (_onFeed is not null) _polling.NewFeedPostReceived -= _onFeed;
        if (_onChannel is not null) _polling.NewChannelMessageReceived -= _onChannel;
        if (_onPollCycle is not null) _polling.PollCycleCompleted -= _onPollCycle;
        if (_onPollError is not null) _polling.PollError -= _onPollError;
        if (_onInitialState is not null) _polling.InitialStateLoaded -= _onInitialState;

        _onComment = null;
        _onInbox = null;
        _onFeed = null;
        _onChannel = null;
        _onPollCycle = null;
        _onPollError = null;
        _onInitialState = null;
    }

    private void AddToAllActivity(ActivityItem item)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            InsertSorted(AllActivity, item);

            // Keep the all-activity list bounded
            while (AllActivity.Count > 500)
                AllActivity.RemoveAt(AllActivity.Count - 1);
        });
    }

    /// <summary>Insert item sorted by Timestamp descending (newest first).</summary>
    private static void InsertSorted(ObservableCollection<ActivityItem> list, ActivityItem item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (item.Timestamp >= list[i].Timestamp)
            {
                list.Insert(i, item);
                return;
            }
        }
        list.Add(item);
    }

    /// <summary>
    /// Navigate to the What's New tab and open the discussion for a given post.
    /// Called when user clicks an inbox item that references a post.
    /// </summary>
    public async Task NavigateToPostDiscussionAsync(string postId)
    {
        AppLogger.Log("VM", $"Navigating to post discussion: {postId}");

        // Switch to What's New tab (index 6)
        SelectedTabIndex = 6;

        // Create a minimal WhatsNewEntry for the post and load its discussion
        var post = await _api.GetPostWithCommentsAsync(postId, CancellationToken.None);
        if (post is null)
        {
            AppLogger.Log("VM", $"Could not load post {postId}");
            return;
        }

        var entry = new WhatsNewEntry
        {
            Id = postId,
            PostId = postId,
            Category = "Post",
            Title = post.Title ?? "(untitled)",
            Description = post.Summary ?? post.Body ?? "",
            Timestamp = DateTimeOffset.Now
        };

        await WhatsNew.LoadDiscussionAsync(entry, CancellationToken.None);
    }

    /// <summary>
    /// Navigate to the Discussions tab and open a specific post's discussion.
    /// Subscribes to the post if not already watched.
    /// </summary>
    public async Task NavigateToDiscussionsTabAsync(string postId)
    {
        AppLogger.Log("VM", $"Navigating to discussions for post: {postId}");

        // Ensure we're subscribed so it appears in the Discussions list
        if (!_watchedPostIds.Contains(postId))
            await SubscribeToPostAsync(postId);

        // Switch to Discussions tab (index 1)
        SelectedTabIndex = 1;

        // Find or wait for the discussion item
        var disc = Comments.Discussions.FirstOrDefault(d => d.PostId == postId);
        if (disc is not null)
        {
            await Comments.LoadDiscussionAsync(disc, CancellationToken.None);
        }
    }

    // ── Subscribe / Unsubscribe (Features 4 & 5) ────────────────

    public async Task SubscribeToPostAsync(string postId)
    {
        if (_watchedPostIds.Contains(postId))
        {
            AppLogger.Log("VM", $"Already subscribed to {postId}");
            return;
        }

        AppLogger.Log("VM", $"Subscribing to post {postId}");

        // Add to watched post IDs
        _watchedPostIds = [.. _watchedPostIds, postId];

        // Update polling
        _polling?.UpdateSettings(_watchedPostIds, _pollIntervalSeconds);

        // Persist settings
        App.SaveLocalSettings(_agentId, string.Join(",", _watchedPostIds), _pollIntervalSeconds,
            _keysDirectory, _claudeApiKey, _newBadgeDurationMinutes);

        // Load post info and add to Discussions list
        var post = await _api.GetPostWithCommentsAsync(postId, CancellationToken.None);
        if (post is not null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!Comments.Discussions.Any(d => d.PostId == postId))
                {
                    Comments.Discussions.Add(new WatchedDiscussionItem
                    {
                        PostId = postId,
                        Title = post.Title ?? post.Summary ?? "(untitled)",
                        Author = post.Author ?? "unknown",
                        CommentCount = post.CommentCount,
                        LastActivity = DateTimeOffset.Now,
                        NewCommentCount = 0
                    });
                }
            });
        }
    }

    public void UnsubscribeFromPost(string postId)
    {
        AppLogger.Log("VM", $"Unsubscribing from post {postId}");

        _watchedPostIds = _watchedPostIds.Where(id => id != postId).ToArray();
        _polling?.UpdateSettings(_watchedPostIds, _pollIntervalSeconds);

        App.SaveLocalSettings(_agentId, string.Join(",", _watchedPostIds), _pollIntervalSeconds,
            _keysDirectory, _claudeApiKey, _newBadgeDurationMinutes);

        Application.Current.Dispatcher.Invoke(() =>
        {
            var disc = Comments.Discussions.FirstOrDefault(d => d.PostId == postId);
            if (disc is not null)
                Comments.Discussions.Remove(disc);
        });
    }

    // ── Agent Lookup (Feature 9) ────────────────────────────────

    public async Task<AgentItem?> LookupAgentAsync(string name)
    {
        if (_agentCache.TryGetValue(name, out var cached))
            return cached;

        try
        {
            var agent = await _api.GetAgentByNameAsync(name, CancellationToken.None);
            if (agent is not null)
                _agentCache[name] = agent;
            return agent;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"VM: Failed to lookup agent '{name}'", ex);
            return null;
        }
    }

    [RelayCommand]
    private void ClearAllActivity()
    {
        AllActivity.Clear();
        Comments.Comments.Clear();
        Inbox.Messages.Clear();
        Feed.Posts.Clear();
    }

    /// <summary>
    /// Re-fetches post content for all currently displayed items after the
    /// ShowFullPosts setting changes. When <paramref name="showFull"/> is true
    /// each post is fetched individually to get the full body; when false the
    /// feed list endpoint is re-fetched (returns summaries) and matched back.
    /// </summary>
    private async Task RefreshPostBodiesAsync(bool showFull, CancellationToken ct)
    {
        // ── Feed / All tab items ─────────────────────────────────────
        var feedItems = AllActivity
            .Where(a => a.Type == ActivityType.FeedPost && !string.IsNullOrEmpty(a.Id))
            .ToList();
        var feedPostItems = Feed.Posts
            .Where(a => a.Type == ActivityType.FeedPost && !string.IsNullOrEmpty(a.Id))
            .ToList();

        // Combine and deduplicate by ID
        var allItems = feedItems.Concat(feedPostItems)
            .GroupBy(a => a.Id)
            .Select(g => g.ToList())
            .ToList();

        if (allItems.Count > 0)
        {
            AppLogger.Log("VM", $"Refreshing post bodies ({(showFull ? "full" : "summary")}) for {allItems.Count} posts...");

            if (showFull)
            {
                // Fetch each post individually for full body
                foreach (var group in allItems)
                {
                    try
                    {
                        var post = await _api.GetPostWithCommentsAsync(group[0].Id, ct);
                        if (post?.Body is not null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                foreach (var item in group)
                                    item.Body = post.Body;
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"VM: failed to refresh body for post {group[0].Id}", ex);
                    }
                }
            }
            else
            {
                // Re-fetch the feed list (without expand=body) to get summaries
                var feed = await _api.GetFeedPostsAsync(null, ct);
                if (feed?.Posts is not null)
                {
                    var lookup = feed.Posts
                        .Where(p => p.Id is not null)
                        .ToDictionary(p => p.Id!, p => p.Summary ?? p.Body ?? "");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var group in allItems)
                        {
                            if (lookup.TryGetValue(group[0].Id, out var summary))
                            {
                                foreach (var item in group)
                                    item.Body = summary;
                            }
                        }
                    });
                }
            }
        }

        // ── Agent posts panel ────────────────────────────────────────
        if (Agents.HasAgentPosts && Agents.AgentPosts.Count > 0)
        {
            if (showFull)
            {
                foreach (var agentPost in Agents.AgentPosts.ToList())
                {
                    try
                    {
                        var post = await _api.GetPostWithCommentsAsync(agentPost.PostId, ct);
                        if (post?.Body is not null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                                agentPost.Summary = post.Body);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"VM: failed to refresh agent post body {agentPost.PostId}", ex);
                    }
                }
            }
            else
            {
                // Re-load agent posts (API will return summaries since ShowFullPosts is now off)
                if (Agents.SelectedAgent is not null)
                    _ = Agents.ReloadAgentPostsAsync(ct);
            }
        }

        AppLogger.Log("VM", "Post body refresh complete");
    }

    [RelayCommand]
    private void OpenOptions()
    {
        var dialog = new SettingsWindow(
            _agentId,
            string.Join(",", _watchedPostIds),
            _pollIntervalSeconds,
            WhatsNew.Options,
            _claudeApiKey,
            _newBadgeDurationMinutes,
            CurrentAgentName,
            CurrentAgentDescription)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            // Apply connection settings — will take effect on next connect
            _watchedPostIds = dialog.WatchedPostIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            _pollIntervalSeconds = dialog.PollIntervalSeconds;
            _claudeApiKey = dialog.ClaudeApiKey;
            _newBadgeDurationMinutes = dialog.NewBadgeDurationMinutes;

            // Apply What's New display options
            WhatsNew.Options.MaxDigestPosts = dialog.MaxDigestPosts;
            WhatsNew.Options.MaxPlatformPosts = dialog.MaxPlatformPosts;
            WhatsNew.Options.MaxAgents = dialog.MaxAgents;
            WhatsNew.Options.MaxSkills = dialog.MaxSkills;

            // Apply Channels tab options
            WhatsNew.Options.MaxChannelsTab = dialog.MaxChannelsTab;
            Channels.MaxChannels = dialog.MaxChannelsTab;

            // Apply Agents tab options
            WhatsNew.Options.MaxAgentsTab = dialog.MaxAgentsTab;
            Agents.MaxAgents = dialog.MaxAgentsTab;

            // Apply post display option
            var fullPostsChanged = WhatsNew.Options.ShowFullPosts != dialog.ShowFullPosts;
            WhatsNew.Options.ShowFullPosts = dialog.ShowFullPosts;
            _api.ShowFullPosts = dialog.ShowFullPosts;

            // Refresh existing post bodies when the setting changes
            if (fullPostsChanged)
                _ = RefreshPostBodiesAsync(dialog.ShowFullPosts, CancellationToken.None);

            // Apply font scale
            WhatsNew.Options.FontScalePercent = dialog.FontScalePercent;
            FontScale = dialog.FontScalePercent / 100.0;

            WhatsNew.SaveOptions();

            // Persist to appsettings.Local.json
            App.SaveLocalSettings(_agentId, string.Join(",", _watchedPostIds), _pollIntervalSeconds,
                _keysDirectory, _claudeApiKey, _newBadgeDurationMinutes);

            AppLogger.Log("VM", $"Options saved: PollInterval={_pollIntervalSeconds}s, " +
                $"MaxDigest={dialog.MaxDigestPosts}, MaxPlatform={dialog.MaxPlatformPosts}, " +
                $"MaxChannelsTab={dialog.MaxChannelsTab}, MaxAgentsTab={dialog.MaxAgentsTab}, " +
                $"ShowFullPosts={dialog.ShowFullPosts}, " +
                $"FontScale={dialog.FontScalePercent}%, BadgeDuration={_newBadgeDurationMinutes}min");
        }
    }
}

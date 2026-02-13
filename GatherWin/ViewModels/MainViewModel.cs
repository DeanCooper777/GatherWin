using System.Collections.ObjectModel;
using System.Windows;
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
    public ObservableCollection<ActivityItem> AllActivity { get; } = new();

    /// <summary>Raised when new activity arrives and the window should flash.</summary>
    public event EventHandler? NewActivityArrived;

    public MainViewModel(
        GatherApiClient api,
        GatherAuthService auth,
        string agentId,
        string[] watchedPostIds,
        int pollIntervalSeconds,
        string keysDirectory)
    {
        _api = api;
        _auth = auth;
        _agentId = agentId;
        _watchedPostIds = watchedPostIds;
        _pollIntervalSeconds = pollIntervalSeconds;
        _keysDirectory = keysDirectory;

        Account = new AccountViewModel();
        Comments = new CommentsViewModel(api);
        Inbox = new InboxViewModel();
        Feed = new FeedViewModel();
        Channels = new ChannelsViewModel(api);
        WhatsNew = new WhatsNewViewModel(api, keysDirectory);

        // Apply saved font scale
        FontScale = WhatsNew.Options.FontScalePercent / 100.0;

        // Watch for token refreshes
        _auth.TokenRefreshed += (_, expiry) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Account.TokenExpiry = expiry;
                TokenExpiryDisplay = $"Token valid until {expiry.ToLocalTime():HH:mm:ss}";
            });
        };
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
                WhatsNew.ResetNewCount();
                // Auto-check when switching to What's New tab (if connected)
                if (IsConnected && !WhatsNew.IsChecking)
                    _ = WhatsNew.CheckForNewsAsync(CancellationToken.None);
                break;
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

            // Fetch watched post stats
            AppLogger.Log("VM", $"Loading stats for {_watchedPostIds.Length} watched posts...");
            await LoadWatchedPostStatsAsync(ct);

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
        Disconnect();
    }

    private async Task LoadWatchedPostStatsAsync(CancellationToken ct)
    {
        Application.Current.Dispatcher.Invoke(() => Account.WatchedPosts.Clear());

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

            Application.Current.Dispatcher.Invoke(() => Account.WatchedPosts.Add(info));
        }
    }

    private void WirePollingEvents(PollingService polling)
    {
        polling.NewCommentReceived += (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            Comments.AddComment(e.PostId, e.PostTitle, e.Author, e.Body, e.Timestamp, isNew);
            AddToAllActivity(new ActivityItem
            {
                Type = ActivityType.Comment,
                Id = e.CommentId,
                Title = e.PostTitle,
                Author = e.Author,
                Body = e.Body,
                Timestamp = e.Timestamp,
                PostId = e.PostId,
                IsNew = isNew
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        polling.NewInboxMessageReceived += (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
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
                IsNew = isNew
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        polling.NewFeedPostReceived += (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            Feed.AddPost(e.PostId, e.Author, e.Title, e.Body, e.Timestamp, isNew);
            AddToAllActivity(new ActivityItem
            {
                Type = ActivityType.FeedPost,
                Id = e.PostId,
                Title = e.Title,
                Author = e.Author,
                Body = e.Body,
                Timestamp = e.Timestamp,
                IsNew = isNew
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        polling.NewChannelMessageReceived += (_, e) =>
        {
            var isNew = !e.IsInitialLoad;
            Channels.AddMessage(e.ChannelId, e.ChannelName, e.Author, e.Body, e.Timestamp, isNew);
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
                IsNew = isNew
            });
            if (isNew) NewActivityArrived?.Invoke(this, EventArgs.Empty);
        };

        polling.PollCycleCompleted += (_, time) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LastPollTime = time;
                StatusMessage = $"Connected — polling every {_pollIntervalSeconds}s | Last: {time.ToLocalTime():HH:mm:ss}";
            });
        };

        polling.PollError += (_, error) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Poll error: {error}";
            });
        };

        polling.InitialStateLoaded += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Connected — polling every {_pollIntervalSeconds}s | Initial state loaded";

                // Auto-select first channel so messages are visible
                if (Channels.SelectedChannel is null && Channels.Channels.Count > 0)
                    Channels.SelectedChannel = Channels.Channels[0];
            });
        };
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

        // Switch to What's New tab (index 5)
        SelectedTabIndex = 5;

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

    [RelayCommand]
    private void ClearAllActivity()
    {
        AllActivity.Clear();
        Comments.Comments.Clear();
        Inbox.Messages.Clear();
        Feed.Posts.Clear();
    }

    [RelayCommand]
    private void OpenOptions()
    {
        var dialog = new SettingsWindow(
            _agentId,
            string.Join(",", _watchedPostIds),
            _pollIntervalSeconds,
            WhatsNew.Options)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            // Apply connection settings — will take effect on next connect
            // (We don't change _agentId at runtime since it requires reconnect)
            _watchedPostIds = dialog.WatchedPostIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            _pollIntervalSeconds = dialog.PollIntervalSeconds;

            // Apply What's New display options
            WhatsNew.Options.MaxDigestPosts = dialog.MaxDigestPosts;
            WhatsNew.Options.MaxPlatformPosts = dialog.MaxPlatformPosts;
            WhatsNew.Options.MaxAgents = dialog.MaxAgents;
            WhatsNew.Options.MaxSkills = dialog.MaxSkills;

            // Apply font scale
            WhatsNew.Options.FontScalePercent = dialog.FontScalePercent;
            FontScale = dialog.FontScalePercent / 100.0;

            WhatsNew.SaveOptions();

            AppLogger.Log("VM", $"Options saved: PollInterval={_pollIntervalSeconds}s, " +
                $"MaxDigest={dialog.MaxDigestPosts}, MaxPlatform={dialog.MaxPlatformPosts}, " +
                $"FontScale={dialog.FontScalePercent}%");
        }
    }
}

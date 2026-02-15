using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class ChannelInfo : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private int _memberCount;
    [ObservableProperty] private int _newMessageCount;
    [ObservableProperty] private string _role = string.Empty;

    public ObservableCollection<ActivityItem> Messages { get; } = new();

    /// <summary>Threaded view of messages (Feature 8).</summary>
    public ObservableCollection<DiscussionComment> ThreadedMessages { get; } = new();
}

public partial class ChannelsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    /// <summary>Callback invoked when a message is successfully sent (first 80 chars).</summary>
    public Action<string, string>? MessageSent { get; set; }

    /// <summary>All channels from the API (unfiltered master list).</summary>
    private readonly List<ChannelInfo> _allChannels = new();

    /// <summary>Filtered channels displayed in the UI.</summary>
    public ObservableCollection<ChannelInfo> Channels { get; } = new();

    [ObservableProperty] private ChannelInfo? _selectedChannel;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private string? _sendError;
    [ObservableProperty] private bool _isLoadingChannels;

    // ── Show All / Subscribe state ───────────────────────────────
    [ObservableProperty] private bool _showAllChannels;
    private HashSet<string> _subscribedChannelIds = new();

    private int _maxChannels = 50;
    public int MaxChannels
    {
        get => _maxChannels;
        set => SetProperty(ref _maxChannels, value);
    }

    /// <summary>Callback to persist subscribed channel IDs.</summary>
    public Action? SubscriptionChanged { get; set; }

    // ── Reply-to state for threaded messages (Feature 8) ─────────
    [ObservableProperty] private DiscussionComment? _replyToMessage;

    // ── Channel Creation State ──────────────────────────────────
    [ObservableProperty] private bool _isCreatingChannel;
    [ObservableProperty] private string _newChannelName = string.Empty;
    [ObservableProperty] private string _newChannelDescription = string.Empty;
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private string? _createError;

    public ChannelsViewModel(GatherApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedChannelChanged(ChannelInfo? value)
    {
        // Auto-refresh member list when switching channels (if panel is open)
        if (ShowChannelDetail && value is not null)
            _ = RefreshChannelDetailAsync(CancellationToken.None);
    }

    partial void OnShowAllChannelsChanged(bool value)
    {
        ApplyFilter();
        SubscriptionChanged?.Invoke();
    }

    public HashSet<string> SubscribedChannelIds => _subscribedChannelIds;

    public void SetSubscribedChannelIds(HashSet<string> ids)
    {
        _subscribedChannelIds = ids;
        ApplyFilter();
    }

    public bool IsSubscribed(string channelId) => _subscribedChannelIds.Contains(channelId);

    public void Subscribe(string channelId)
    {
        if (_subscribedChannelIds.Add(channelId))
        {
            OnPropertyChanged(nameof(SubscribedChannelIds));
            SubscriptionChanged?.Invoke();
            // Don't re-filter — the channel is already visible (user is looking at it)
        }
    }

    public void Unsubscribe(string channelId)
    {
        if (_subscribedChannelIds.Remove(channelId))
        {
            OnPropertyChanged(nameof(SubscribedChannelIds));
            SubscriptionChanged?.Invoke();
            // Don't re-filter — keep the channel visible so the user doesn't lose their discussion.
            // The filter will apply next time ShowAllChannels changes or channels are reloaded.
        }
    }

    /// <summary>Load all channels from the API, and pre-load messages for subscribed channels.</summary>
    public async Task LoadAllChannelsAsync(CancellationToken ct)
    {
        if (IsLoadingChannels) return;
        IsLoadingChannels = true;

        try
        {
            var response = await _api.GetChannelsAsync(ct);
            if (response?.Channels is not null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _allChannels.Clear();
                    foreach (var ch in response.Channels.Take(MaxChannels))
                    {
                        var info = _allChannels.FirstOrDefault(c => c.Id == ch.Id)
                            ?? Channels.FirstOrDefault(c => c.Id == ch.Id);
                        if (info is null)
                        {
                            info = new ChannelInfo
                            {
                                Id = ch.Id ?? "",
                                Name = ch.Name ?? "",
                                Description = ch.Description ?? "",
                                MemberCount = ch.MemberCount,
                                Role = ch.Role ?? ""
                            };
                        }
                        else
                        {
                            info.Name = ch.Name ?? info.Name;
                            info.Description = ch.Description ?? info.Description;
                            info.MemberCount = ch.MemberCount;
                            info.Role = ch.Role ?? info.Role;
                        }
                        _allChannels.Add(info);
                    }
                    ApplyFilter();
                });

                AppLogger.Log("Channels", $"Loaded {response.Channels.Count} channels (max={MaxChannels})");

                // Pre-load messages for all visible channels so they're ready immediately
                // (not just subscribed — the user may have ShowAllChannels enabled)
                var channelsToPreload = _allChannels.ToList();
                AppLogger.Log("Channels", $"Pre-loading messages for {channelsToPreload.Count} channels...");

                foreach (var ch in channelsToPreload)
                {
                    try
                    {
                        AppLogger.Log("Channels", $"Fetching messages for #{ch.Name} (id={ch.Id})...");
                        var msgResp = await _api.GetChannelMessagesAsync(ch.Id, null, ct);
                        AppLogger.Log("Channels", $"  Response for #{ch.Name}: {(msgResp is null ? "NULL" : $"{msgResp.Messages?.Count ?? 0} messages")}");
                        if (msgResp?.Messages is not null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                // Reverse so oldest is first (chat-style: newest at bottom)
                                foreach (var m in msgResp.Messages.AsEnumerable().Reverse())
                                {
                                    var threadedMsg = new DiscussionComment
                                    {
                                        CommentId = m.Id ?? "",
                                        Author = m.AuthorName ?? m.AuthorId ?? "unknown",
                                        Body = m.Body ?? "(empty)",
                                        Timestamp = ParseTimestamp(m.Created),
                                        IndentLevel = 0,
                                        ReplyToId = m.ReplyTo
                                    };

                                    // Build thread hierarchy
                                    if (!string.IsNullOrEmpty(m.ReplyTo))
                                    {
                                        var parentIdx = -1;
                                        for (int i = 0; i < ch.ThreadedMessages.Count; i++)
                                        {
                                            if (ch.ThreadedMessages[i].CommentId == m.ReplyTo)
                                            {
                                                parentIdx = i;
                                                threadedMsg.IndentLevel = ch.ThreadedMessages[i].IndentLevel + 1;
                                                break;
                                            }
                                        }
                                        if (parentIdx >= 0)
                                        {
                                            int insertAt = parentIdx + 1;
                                            while (insertAt < ch.ThreadedMessages.Count &&
                                                   ch.ThreadedMessages[insertAt].IndentLevel > ch.ThreadedMessages[parentIdx].IndentLevel)
                                                insertAt++;
                                            ch.ThreadedMessages.Insert(insertAt, threadedMsg);
                                        }
                                        else
                                        {
                                            ch.ThreadedMessages.Add(threadedMsg);
                                        }
                                    }
                                    else
                                    {
                                        ch.ThreadedMessages.Add(threadedMsg);
                                    }

                                    // Also add to flat Messages collection
                                    var item = new ActivityItem
                                    {
                                        Type = ActivityType.Channel,
                                        Id = m.Id ?? "",
                                        Title = ch.Name,
                                        Author = m.AuthorName ?? m.AuthorId ?? "unknown",
                                        Body = m.Body ?? "(empty)",
                                        Timestamp = ParseTimestamp(m.Created),
                                        ChannelId = ch.Id,
                                        ChannelName = ch.Name,
                                        IsNew = false
                                    };
                                    InsertSorted(ch.Messages, item);
                                }
                            });

                            AppLogger.Log("Channels", $"Pre-loaded {msgResp.Messages.Count} messages for #{ch.Name} — ThreadedMessages.Count={ch.ThreadedMessages.Count}, Messages.Count={ch.Messages.Count}");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"Channels: failed to pre-load messages for #{ch.Name}", ex);
                    }
                }

                // Log state of Channels collection
                AppLogger.Log("Channels", $"After pre-load: Channels.Count={Channels.Count}, _allChannels.Count={_allChannels.Count}");
                foreach (var ch in Channels)
                    AppLogger.Log("Channels", $"  Channel '{ch.Name}' in Channels: ThreadedMessages={ch.ThreadedMessages.Count}, Messages={ch.Messages.Count}");

                // Auto-select the first visible channel
                if (SelectedChannel is null && Channels.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SelectedChannel = Channels[0];
                    });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("Channels: load all channels failed", ex);
        }
        finally
        {
            IsLoadingChannels = false;
        }
    }

    private static DateTimeOffset ParseTimestamp(string? ts)
    {
        if (ts is not null && DateTimeOffset.TryParse(ts, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }

    private void ApplyFilter()
    {
        void DoFilter()
        {
            var selected = SelectedChannel;
            Channels.Clear();

            var source = ShowAllChannels
                ? _allChannels
                : _allChannels.Where(c => _subscribedChannelIds.Contains(c.Id)).ToList();

            foreach (var ch in source)
                Channels.Add(ch);

            // Restore selection if possible
            if (selected is not null)
                SelectedChannel = Channels.FirstOrDefault(c => c.Id == selected.Id);
        }

        if (Application.Current?.Dispatcher is not null)
            Application.Current.Dispatcher.Invoke(DoFilter);
        else
            DoFilter();
    }

    /// <summary>Add a newly discovered channel to the master list and refresh the filter.</summary>
    public void AddDiscoveredChannel(ChannelInfo info)
    {
        if (_allChannels.Any(c => c.Id == info.Id))
            return;

        _allChannels.Add(info);
        ApplyFilter();

        AppLogger.Log("Channels", $"Discovered new channel: #{info.Name} ({info.Id})");
    }

    public void AddMessage(string channelId, string channelName, string messageId, string author,
        string body, DateTimeOffset timestamp, bool isNew = true, string? replyTo = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Find channel in filtered list, master list, or create new
            var channel = Channels.FirstOrDefault(c => c.Id == channelId)
                ?? _allChannels.FirstOrDefault(c => c.Id == channelId);
            if (channel is null)
            {
                channel = new ChannelInfo { Id = channelId, Name = channelName };
                _allChannels.Add(channel);
                Channels.Add(channel);
            }

            // Skip duplicates — check by ID and also by content (handles optimistic local adds
            // where the local GUID differs from the server-assigned ID)
            if (channel.Messages.Any(m => m.Id == messageId))
                return;
            if (channel.Messages.Any(m => m.Author == author && m.Body == body
                    && Math.Abs((m.Timestamp - timestamp).TotalSeconds) < 60))
                return;

            var item = new ActivityItem
            {
                Type = ActivityType.Channel,
                Id = messageId,
                Title = channelName,
                Author = author,
                Body = body,
                Timestamp = timestamp,
                ChannelId = channelId,
                ChannelName = channelName,
                IsNew = isNew,
                MarkedNewAt = isNew ? DateTimeOffset.Now : default
            };

            InsertSorted(channel.Messages, item);

            // Also add to threaded view (skip if content-duplicate already exists)
            if (channel.ThreadedMessages.Any(m => m.Author == author && m.Body == body
                    && Math.Abs((m.Timestamp - timestamp).TotalSeconds) < 60))
            {
                if (isNew)
                {
                    channel.NewMessageCount++;
                    NewCount++;
                }
                return;
            }

            var threadedMsg = new DiscussionComment
            {
                CommentId = messageId,
                Author = author,
                Body = body,
                Timestamp = timestamp,
                IndentLevel = 0,
                ReplyToId = replyTo,
                IsNew = isNew
            };

            if (!string.IsNullOrEmpty(replyTo))
            {
                // Find parent and insert after it with indent
                var parentIdx = -1;
                for (int i = 0; i < channel.ThreadedMessages.Count; i++)
                {
                    if (channel.ThreadedMessages[i].CommentId == replyTo)
                    {
                        parentIdx = i;
                        threadedMsg.IndentLevel = channel.ThreadedMessages[i].IndentLevel + 1;
                        break;
                    }
                }

                if (parentIdx >= 0)
                {
                    int insertAt = parentIdx + 1;
                    while (insertAt < channel.ThreadedMessages.Count &&
                           channel.ThreadedMessages[insertAt].IndentLevel > channel.ThreadedMessages[parentIdx].IndentLevel)
                    {
                        insertAt++;
                    }
                    channel.ThreadedMessages.Insert(insertAt, threadedMsg);
                }
                else
                {
                    channel.ThreadedMessages.Add(threadedMsg);
                }
            }
            else
            {
                // Append at bottom (chat-style: oldest top, newest bottom)
                channel.ThreadedMessages.Add(threadedMsg);
            }

            if (isNew)
            {
                channel.NewMessageCount++;
                NewCount++;
            }
        });
    }

    public void ResetNewCount() => NewCount = 0;

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

    public void SetReplyTo(DiscussionComment? message)
    {
        ReplyToMessage = message;
        SendError = null;
    }

    [RelayCommand]
    private void CancelChannelReplyTo()
    {
        ReplyToMessage = null;
    }

    [RelayCommand]
    private async Task SendMessageAsync(CancellationToken ct)
    {
        if (IsSending) return; // Guard against double-click

        if (SelectedChannel is null || string.IsNullOrWhiteSpace(ReplyText))
            return;

        if (ReplyText.Trim().Length > Converters.CharLimitSettings.ChannelMaxLength)
        {
            SendError = $"Message exceeds {Converters.CharLimitSettings.ChannelMaxLength:N0} character limit ({ReplyText.Trim().Length:N0} chars). Please shorten your message.";
            return;
        }

        IsSending = true;
        SendError = null;

        try
        {
            var replyToId = ReplyToMessage?.CommentId;
            var messageBody = "[Human] " + ReplyText.Trim();
            var (success, error) = await _api.PostChannelMessageAsync(
                SelectedChannel.Id, messageBody, ct, replyToId);

            if (success)
            {
                var msg = new ActivityItem
                {
                    Type = ActivityType.Channel,
                    Id = Guid.NewGuid().ToString(),
                    Title = SelectedChannel.Name,
                    Author = "OnTheEdgeOfReality",
                    Body = messageBody,
                    Timestamp = DateTimeOffset.Now,
                    ChannelId = SelectedChannel.Id,
                    ChannelName = SelectedChannel.Name,
                    IsNew = false
                };

                var threadedMsg = new DiscussionComment
                {
                    CommentId = msg.Id,
                    Author = "OnTheEdgeOfReality",
                    Body = messageBody,
                    Timestamp = DateTimeOffset.Now,
                    IndentLevel = ReplyToMessage?.IndentLevel + 1 ?? 0,
                    ReplyToId = replyToId
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    SelectedChannel.Messages.Insert(0, msg);

                    if (ReplyToMessage is not null)
                    {
                        var parentIdx = SelectedChannel.ThreadedMessages.IndexOf(ReplyToMessage);
                        if (parentIdx >= 0)
                        {
                            int insertAt = parentIdx + 1;
                            while (insertAt < SelectedChannel.ThreadedMessages.Count &&
                                   SelectedChannel.ThreadedMessages[insertAt].IndentLevel > ReplyToMessage.IndentLevel)
                            {
                                insertAt++;
                            }
                            SelectedChannel.ThreadedMessages.Insert(insertAt, threadedMsg);
                        }
                        else
                        {
                            SelectedChannel.ThreadedMessages.Add(threadedMsg);
                        }
                    }
                    else
                    {
                        SelectedChannel.ThreadedMessages.Add(threadedMsg);
                    }
                });

                // Clear NEW badges on this channel after sending
                foreach (var m in SelectedChannel.ThreadedMessages)
                {
                    if (m.IsNew) m.IsNew = false;
                }
                SelectedChannel.NewMessageCount = 0;

                var sentText = ReplyText.Trim();
                ReplyText = string.Empty;
                ReplyToMessage = null;

                MessageSent?.Invoke(SelectedChannel.Name, sentText);
            }
            else
            {
                SendError = error ?? "Failed to send message";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Channels: send message failed", ex);
            SendError = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }

    // ── Channel Creation ────────────────────────────────────────

    [RelayCommand]
    private void ShowCreateChannel()
    {
        IsCreatingChannel = true;
        NewChannelName = string.Empty;
        NewChannelDescription = string.Empty;
        CreateError = null;
    }

    [RelayCommand]
    private void CancelCreateChannel()
    {
        IsCreatingChannel = false;
        NewChannelName = string.Empty;
        NewChannelDescription = string.Empty;
        CreateError = null;
    }

    [RelayCommand]
    private async Task CreateChannelAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewChannelName))
        {
            CreateError = "Channel name is required";
            return;
        }

        IsCreating = true;
        CreateError = null;

        try
        {
            var description = string.IsNullOrWhiteSpace(NewChannelDescription)
                ? null : NewChannelDescription.Trim();

            var (success, channelId, error) = await _api.CreateChannelAsync(
                NewChannelName.Trim(), description, ct);

            if (success)
            {
                AppLogger.Log("Channels", $"Created channel '{NewChannelName.Trim()}' (id={channelId})");

                var newChannel = new ChannelInfo
                {
                    Id = channelId ?? Guid.NewGuid().ToString(),
                    Name = NewChannelName.Trim(),
                    Description = description ?? string.Empty,
                    MemberCount = 1
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Channels.Add(newChannel);
                    SelectedChannel = newChannel;
                });

                IsCreatingChannel = false;
                NewChannelName = string.Empty;
                NewChannelDescription = string.Empty;
            }
            else
            {
                CreateError = error ?? "Failed to create channel";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Channels: create channel failed", ex);
            CreateError = ex.Message;
        }
        finally
        {
            IsCreating = false;
        }
    }

    // ── Channel Invite (Task #10) ────────────────────────────────

    [ObservableProperty] private bool _isInviting;
    [ObservableProperty] private string _inviteAgentName = string.Empty;
    [ObservableProperty] private string? _inviteError;
    [ObservableProperty] private string? _inviteSuccess;
    [ObservableProperty] private bool _showInviteForm;

    [RelayCommand]
    private void ShowInvite()
    {
        ShowInviteForm = true;
        InviteError = null;
        InviteSuccess = null;
        InviteAgentName = string.Empty;
    }

    [RelayCommand]
    private void CancelInvite()
    {
        ShowInviteForm = false;
        InviteAgentName = string.Empty;
        InviteError = null;
        InviteSuccess = null;
    }

    [RelayCommand]
    private async Task InviteAgentAsync(CancellationToken ct)
    {
        if (SelectedChannel is null || string.IsNullOrWhiteSpace(InviteAgentName))
        {
            InviteError = "Enter an agent name";
            return;
        }

        IsInviting = true;
        InviteError = null;
        InviteSuccess = null;

        try
        {
            // Resolve agent name to ID
            var agent = await _api.GetAgentByNameAsync(InviteAgentName.Trim(), ct);
            if (agent is null)
            {
                InviteError = $"Agent \"{InviteAgentName.Trim()}\" not found";
                return;
            }

            var (success, error) = await _api.InviteToChannelAsync(SelectedChannel.Id, agent.Id!, ct);
            if (success)
            {
                InviteSuccess = $"Invited {InviteAgentName.Trim()}!";
                InviteAgentName = string.Empty;
                AppLogger.Log("Channels", $"Invited {agent.Name} to #{SelectedChannel.Name}");
            }
            else
            {
                InviteError = error ?? "Failed to invite agent";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Channels: invite agent failed", ex);
            InviteError = ex.Message;
        }
        finally
        {
            IsInviting = false;
        }
    }

    // ── Channel Details / Member List (Task #11) ─────────────────

    [ObservableProperty] private bool _isLoadingDetail;
    [ObservableProperty] private bool _showChannelDetail;
    public ObservableCollection<ChannelMember> ChannelMembers { get; } = new();

    [RelayCommand]
    private async Task ToggleChannelDetailAsync(CancellationToken ct)
    {
        ShowChannelDetail = !ShowChannelDetail;
        if (!ShowChannelDetail)
        {
            Application.Current.Dispatcher.Invoke(() => ChannelMembers.Clear());
            return;
        }
        await RefreshChannelDetailAsync(ct);
    }

    private async Task RefreshChannelDetailAsync(CancellationToken ct)
    {
        if (SelectedChannel is null) return;

        IsLoadingDetail = true;

        try
        {
            var detail = await _api.GetChannelDetailAsync(SelectedChannel.Id, ct);
            Application.Current.Dispatcher.Invoke(() =>
            {
                ChannelMembers.Clear();
                if (detail?.Members is not null)
                {
                    foreach (var m in detail.Members)
                        ChannelMembers.Add(m);
                    SelectedChannel.MemberCount = detail.Members.Count;
                }
            });

            if (detail?.Members is not null)
                AppLogger.Log("Channels", $"Loaded {detail.Members.Count} members for #{SelectedChannel.Name}");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Channels: load detail failed", ex);
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }
}

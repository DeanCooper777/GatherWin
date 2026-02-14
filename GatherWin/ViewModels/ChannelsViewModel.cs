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

    public ObservableCollection<ActivityItem> Messages { get; } = new();

    /// <summary>Threaded view of messages (Feature 8).</summary>
    public ObservableCollection<DiscussionComment> ThreadedMessages { get; } = new();
}

public partial class ChannelsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<ChannelInfo> Channels { get; } = new();

    [ObservableProperty] private ChannelInfo? _selectedChannel;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private string? _sendError;

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

    public void AddMessage(string channelId, string channelName, string messageId, string author,
        string body, DateTimeOffset timestamp, bool isNew = true, string? replyTo = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Find or create channel
            var channel = Channels.FirstOrDefault(c => c.Id == channelId);
            if (channel is null)
            {
                channel = new ChannelInfo { Id = channelId, Name = channelName };
                Channels.Add(channel);
            }

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

            // Also add to threaded view
            var threadedMsg = new DiscussionComment
            {
                CommentId = messageId,
                Author = author,
                Body = body,
                Timestamp = timestamp,
                IndentLevel = 0,
                ReplyToId = replyTo
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
        if (SelectedChannel is null || string.IsNullOrWhiteSpace(ReplyText))
            return;

        IsSending = true;
        SendError = null;

        try
        {
            var replyToId = ReplyToMessage?.CommentId;
            var (success, error) = await _api.PostChannelMessageAsync(
                SelectedChannel.Id, ReplyText.Trim(), ct, replyToId);

            if (success)
            {
                var msg = new ActivityItem
                {
                    Type = ActivityType.Channel,
                    Id = Guid.NewGuid().ToString(),
                    Title = SelectedChannel.Name,
                    Author = "OnTheEdgeOfReality",
                    Body = ReplyText.Trim(),
                    Timestamp = DateTimeOffset.Now,
                    ChannelId = SelectedChannel.Id,
                    ChannelName = SelectedChannel.Name,
                    IsNew = false
                };

                var threadedMsg = new DiscussionComment
                {
                    CommentId = msg.Id,
                    Author = "OnTheEdgeOfReality",
                    Body = ReplyText.Trim(),
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

                ReplyText = string.Empty;
                ReplyToMessage = null;
            }
            else
            {
                SendError = error ?? "Failed to send message";
            }
        }
        catch (Exception ex)
        {
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
            CreateError = ex.Message;
        }
        finally
        {
            IsCreating = false;
        }
    }
}

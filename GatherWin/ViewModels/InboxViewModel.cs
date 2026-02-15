using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class InboxViewModel : ObservableObject
{
    private readonly GatherApiClient? _api;

    public ObservableCollection<ActivityItem> Messages { get; } = new();

    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private ActivityItem? _selectedMessage;

    /// <summary>
    /// Update unread count from the API. We use the local count of new items
    /// (items with IsNew=true) as the source of truth, since the server count
    /// may lag behind local read/pre-load state.
    /// </summary>
    public void UpdateUnreadFromApi(int apiCount)
    {
        // Count locally-new items (genuinely unread in the UI)
        var localNew = Messages.Count(m => m.IsNew);
        UnreadCount = localNew;
    }

    public InboxViewModel() { }
    public InboxViewModel(GatherApiClient api) { _api = api; }

    public void AddMessage(string messageId, string subject, string body, DateTimeOffset timestamp, bool isNew = true,
        string? postId = null, string? commentId = null, string? channelId = null)
    {
        // Deduplicate by ID
        if (!string.IsNullOrEmpty(messageId) && Messages.Any(m => m.Id == messageId))
            return;
        // Also deduplicate by content (handles different IDs for same notification)
        if (Messages.Any(m => m.Title == subject && m.Body == body
                && Math.Abs((m.Timestamp - timestamp).TotalSeconds) < 60))
            return;

        var item = new ActivityItem
        {
            Type = ActivityType.Inbox,
            Id = messageId,
            Title = subject,
            Author = string.Empty,
            Body = body,
            Timestamp = timestamp,
            IsNew = isNew,
            MarkedNewAt = isNew ? DateTimeOffset.Now : default,
            PostId = postId,
            CommentId = commentId,
            ChannelId = channelId
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            InsertSorted(Messages, item);
            if (isNew) NewCount++;
        });
    }

    public async Task MarkReadAsync(ActivityItem message)
    {
        if (_api is null || string.IsNullOrEmpty(message.Id)) return;
        var (success, error) = await _api.MarkInboxReadAsync(message.Id, CancellationToken.None);
        if (success)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                message.IsNew = false;
                UnreadCount = Messages.Count(m => m.IsNew);
            });
        }
        else
        {
            AppLogger.LogError($"Mark read failed: {error}");
        }
    }

    public async Task DeleteMessageAsync(ActivityItem message)
    {
        if (_api is null || string.IsNullOrEmpty(message.Id)) return;
        var (success, error) = await _api.DeleteInboxMessageAsync(message.Id, CancellationToken.None);
        if (success)
        {
            Application.Current.Dispatcher.Invoke(() => Messages.Remove(message));
        }
        else
        {
            AppLogger.LogError($"Delete inbox message failed: {error}");
        }
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
}

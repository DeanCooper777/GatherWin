using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using GatherWin.Models;

namespace GatherWin.ViewModels;

public partial class InboxViewModel : ObservableObject
{
    public ObservableCollection<ActivityItem> Messages { get; } = new();

    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private ActivityItem? _selectedMessage;

    public void AddMessage(string subject, string body, DateTimeOffset timestamp, bool isNew = true,
        string? postId = null, string? commentId = null, string? channelId = null)
    {
        var item = new ActivityItem
        {
            Type = ActivityType.Inbox,
            Id = Guid.NewGuid().ToString(),
            Title = subject,
            Author = string.Empty,
            Body = body,
            Timestamp = timestamp,
            IsNew = isNew,
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

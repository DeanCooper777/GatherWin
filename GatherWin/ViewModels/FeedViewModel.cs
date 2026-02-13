using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using GatherWin.Models;

namespace GatherWin.ViewModels;

public partial class FeedViewModel : ObservableObject
{
    public ObservableCollection<ActivityItem> Posts { get; } = new();

    [ObservableProperty] private int _newCount;
    [ObservableProperty] private ActivityItem? _selectedPost;

    public void AddPost(string postId, string author, string title, string body, DateTimeOffset timestamp, bool isNew = true)
    {
        var item = new ActivityItem
        {
            Type = ActivityType.FeedPost,
            Id = postId,
            Title = title,
            Author = author,
            Body = body,
            Timestamp = timestamp,
            IsNew = isNew
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            InsertSorted(Posts, item);
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

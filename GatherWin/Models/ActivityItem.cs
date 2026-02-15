using CommunityToolkit.Mvvm.ComponentModel;

namespace GatherWin.Models;

public enum ActivityType
{
    Comment,
    Inbox,
    FeedPost,
    Channel
}

public partial class ActivityItem : ObservableObject
{
    [ObservableProperty] private ActivityType _type;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private DateTimeOffset _timestamp;
    [ObservableProperty] private string? _postId;
    [ObservableProperty] private string? _commentId;
    [ObservableProperty] private string? _channelId;
    [ObservableProperty] private string? _channelName;
    [ObservableProperty] private int _score;
    [ObservableProperty] private bool _isNew = true;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>When this item was marked as new (for badge timeout).</summary>
    [ObservableProperty] private DateTimeOffset _markedNewAt;

    /// <summary>
    /// Force WPF to re-evaluate the Timestamp binding (refreshes relative time display).
    /// </summary>
    public void RefreshTimestamp() => OnPropertyChanged(nameof(Timestamp));
}

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class CommentsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<ActivityItem> Comments { get; } = new();

    [ObservableProperty] private ActivityItem? _selectedComment;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private string? _sendError;

    public CommentsViewModel(GatherApiClient api)
    {
        _api = api;
    }

    public void AddComment(string postId, string postTitle, string author, string body, DateTimeOffset timestamp, bool isNew = true)
    {
        var item = new ActivityItem
        {
            Type = ActivityType.Comment,
            Id = Guid.NewGuid().ToString(),
            Title = postTitle,
            Author = author,
            Body = body,
            Timestamp = timestamp,
            PostId = postId,
            IsNew = isNew
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            InsertSorted(Comments, item);
            if (isNew) NewCount++;
        });
    }

    public void ResetNewCount() => NewCount = 0;

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

    [RelayCommand]
    private async Task SendReplyAsync(CancellationToken ct)
    {
        if (SelectedComment?.PostId is null || string.IsNullOrWhiteSpace(ReplyText))
            return;

        IsSending = true;
        SendError = null;

        try
        {
            var (success, error) = await _api.PostCommentAsync(SelectedComment.PostId, ReplyText.Trim(), ct);

            if (success)
            {
                // Add the reply optimistically to the local list
                var reply = new ActivityItem
                {
                    Type = ActivityType.Comment,
                    Id = Guid.NewGuid().ToString(),
                    Title = SelectedComment.Title,
                    Author = "OnTheEdgeOfReality",
                    Body = ReplyText.Trim(),
                    Timestamp = DateTimeOffset.Now,
                    PostId = SelectedComment.PostId,
                    IsNew = false
                };

                Application.Current.Dispatcher.Invoke(() => Comments.Insert(0, reply));
                ReplyText = string.Empty;
            }
            else
            {
                SendError = error ?? "Failed to send reply";
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
}

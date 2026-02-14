using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class FeedViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<ActivityItem> Posts { get; } = new();

    [ObservableProperty] private int _newCount;
    [ObservableProperty] private ActivityItem? _selectedPost;

    // ── Post Creation State ──────────────────────────────────────
    [ObservableProperty] private bool _isComposing;
    [ObservableProperty] private string _newPostTitle = string.Empty;
    [ObservableProperty] private string _newPostBody = string.Empty;
    [ObservableProperty] private string _newPostTags = string.Empty;
    [ObservableProperty] private bool _isPosting;
    [ObservableProperty] private string? _postError;

    public FeedViewModel(GatherApiClient api)
    {
        _api = api;
    }

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

    // ── Post Creation Commands ───────────────────────────────────

    [RelayCommand]
    private void ShowCompose()
    {
        IsComposing = true;
        PostError = null;
    }

    [RelayCommand]
    private void CancelCompose()
    {
        IsComposing = false;
        NewPostTitle = string.Empty;
        NewPostBody = string.Empty;
        NewPostTags = string.Empty;
        PostError = null;
    }

    [RelayCommand]
    private async Task CreatePostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewPostTitle) || string.IsNullOrWhiteSpace(NewPostBody))
        {
            PostError = "Title and body are required";
            return;
        }

        var tags = NewPostTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .Take(5)
            .ToList();
        if (tags.Count == 0)
        {
            PostError = "At least one tag is required (comma-separated, max 5)";
            return;
        }

        IsPosting = true;
        PostError = null;

        try
        {
            var (success, postId, error) = await _api.CreatePostAsync(
                NewPostTitle.Trim(), NewPostBody.Trim(), tags, ct);

            if (success)
            {
                AppLogger.Log("Feed", $"Post created: \"{NewPostTitle.Trim()}\"");

                // Add optimistically to local list
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var newItem = new ActivityItem
                    {
                        Type = ActivityType.FeedPost,
                        Id = postId ?? Guid.NewGuid().ToString(),
                        Title = NewPostTitle.Trim(),
                        Author = "OnTheEdgeOfReality",
                        Body = NewPostBody.Trim(),
                        Timestamp = DateTimeOffset.Now,
                        IsNew = false
                    };
                    InsertSorted(Posts, newItem);
                });

                // Clear form
                NewPostTitle = string.Empty;
                NewPostBody = string.Empty;
                NewPostTags = string.Empty;
                IsComposing = false;
            }
            else
            {
                PostError = error ?? "Failed to create post";
            }
        }
        catch (Exception ex)
        {
            PostError = ex.Message;
        }
        finally
        {
            IsPosting = false;
        }
    }

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

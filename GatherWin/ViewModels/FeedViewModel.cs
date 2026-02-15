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
    [ObservableProperty] private bool _sortByScore;

    /// <summary>Callback to refresh feed with new sort order (wired by MainViewModel).</summary>
    public Action? SortChanged { get; set; }

    public string SortLabel => SortByScore ? "Top" : "New";

    partial void OnSortByScoreChanged(bool value)
    {
        OnPropertyChanged(nameof(SortLabel));
        SortChanged?.Invoke();
    }

    // ── Post Creation State ──────────────────────────────────────
    [ObservableProperty] private bool _isComposing;
    [ObservableProperty] private string _newPostTitle = string.Empty;
    [ObservableProperty] private string _newPostBody = string.Empty;
    [ObservableProperty] private string _newPostTags = string.Empty;
    [ObservableProperty] private bool _isPosting;
    [ObservableProperty] private string? _postError;

    // ── Discussion Panel State (Feature 4) ───────────────────────
    [ObservableProperty] private string? _discussionPostId;
    [ObservableProperty] private string _discussionTitle = string.Empty;
    [ObservableProperty] private string _discussionBody = string.Empty;
    [ObservableProperty] private string _discussionAuthor = string.Empty;
    [ObservableProperty] private bool _isLoadingDiscussion;
    [ObservableProperty] private bool _hasDiscussion;
    [ObservableProperty] private string _discussionReplyText = string.Empty;
    [ObservableProperty] private bool _isSendingDiscussionReply;
    [ObservableProperty] private string? _discussionSendError;
    [ObservableProperty] private DiscussionComment? _replyToComment;
    [ObservableProperty] private bool _isPostBodyExpanded;
    [ObservableProperty] private bool _isReplyExpanded;

    public ObservableCollection<DiscussionComment> DiscussionComments { get; } = new();

    /// <summary>Callback to subscribe to a post (wired by MainViewModel).</summary>
    public Action<string>? SubscribeRequested { get; set; }

    public FeedViewModel(GatherApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    private void ToggleSort() => SortByScore = !SortByScore;

    public void AddPost(string postId, string author, string title, string body, DateTimeOffset timestamp, bool isNew = true, int score = 0)
    {
        var item = new ActivityItem
        {
            Type = ActivityType.FeedPost,
            Id = postId,
            Title = title,
            Author = author,
            Body = body,
            Timestamp = timestamp,
            Score = score,
            IsNew = isNew,
            MarkedNewAt = isNew ? DateTimeOffset.Now : default
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
            var postBody = "[Human] " + NewPostBody.Trim();
            var (success, postId, error) = await _api.CreatePostAsync(
                NewPostTitle.Trim(), postBody, tags, ct);

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
                        Body = postBody,
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
            AppLogger.LogError("Feed: create post failed", ex);
            PostError = ex.Message;
        }
        finally
        {
            IsPosting = false;
        }
    }

    // ── Discussion Panel Methods (Feature 4) ─────────────────────

    public async Task LoadDiscussionAsync(ActivityItem post, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(post.Id)) return;

        DiscussionPostId = post.Id;
        DiscussionTitle = post.Title;
        DiscussionBody = string.Empty;
        DiscussionAuthor = post.Author;
        IsLoadingDiscussion = true;
        HasDiscussion = true;
        DiscussionSendError = null;
        DiscussionReplyText = string.Empty;
        ReplyToComment = null;
        IsPostBodyExpanded = false;
        IsReplyExpanded = false;

        Application.Current.Dispatcher.Invoke(() => DiscussionComments.Clear());

        try
        {
            var fullPost = await _api.GetPostWithCommentsAsync(post.Id, ct);
            if (fullPost is null)
            {
                DiscussionBody = "(Failed to load post)";
                return;
            }

            DiscussionTitle = fullPost.Title ?? fullPost.Summary ?? "(untitled)";
            DiscussionBody = fullPost.Body ?? fullPost.Summary ?? "(no body)";
            DiscussionAuthor = fullPost.Author ?? "unknown";

            var comments = fullPost.Comments ?? [];
            var commentMap = comments.Where(c => c.Id is not null).ToDictionary(c => c.Id!);
            var topLevel = comments.Where(c => string.IsNullOrEmpty(c.ReplyTo)).ToList();
            var replies = comments.Where(c => !string.IsNullOrEmpty(c.ReplyTo)).ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var c in topLevel.OrderBy(c => ParseTimestamp(c.Created)))
                {
                    DiscussionComments.Add(new DiscussionComment
                    {
                        CommentId = c.Id ?? "",
                        Author = c.Author ?? "unknown",
                        Body = c.Body ?? "(empty)",
                        Timestamp = ParseTimestamp(c.Created),
                        IsVerified = c.Verified,
                        IndentLevel = 0
                    });

                    AddReplies(c.Id!, replies, commentMap, 1);
                }
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("Feed: load discussion failed", ex);
            DiscussionBody = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingDiscussion = false;
        }
    }

    private void AddReplies(string parentId, List<GatherComment> allReplies, Dictionary<string, GatherComment> commentMap, int depth)
    {
        var childReplies = allReplies.Where(r => r.ReplyTo == parentId).OrderBy(r => ParseTimestamp(r.Created));
        foreach (var reply in childReplies)
        {
            DiscussionComments.Add(new DiscussionComment
            {
                CommentId = reply.Id ?? "",
                Author = reply.Author ?? "unknown",
                Body = reply.Body ?? "(empty)",
                Timestamp = ParseTimestamp(reply.Created),
                IsVerified = reply.Verified,
                IndentLevel = depth,
                ReplyToId = parentId
            });

            if (reply.Id is not null)
                AddReplies(reply.Id, allReplies, commentMap, depth + 1);
        }
    }

    public void CloseDiscussion()
    {
        HasDiscussion = false;
        DiscussionPostId = null;
        ReplyToComment = null;
        Application.Current.Dispatcher.Invoke(() => DiscussionComments.Clear());
    }

    public void SetReplyTo(DiscussionComment? comment)
    {
        ReplyToComment = comment;
        DiscussionSendError = null;
    }

    [RelayCommand]
    private void CancelFeedReplyTo()
    {
        ReplyToComment = null;
    }

    [RelayCommand]
    private async Task SendFeedReplyAsync(CancellationToken ct)
    {
        if (IsSendingDiscussionReply) return; // Guard against double-click

        if (string.IsNullOrEmpty(DiscussionPostId) || string.IsNullOrWhiteSpace(DiscussionReplyText))
            return;

        if (DiscussionReplyText.Trim().Length > Converters.CharLimitSettings.MaxLength)
        {
            DiscussionSendError = $"Comment exceeds {Converters.CharLimitSettings.MaxLength:N0} character limit ({DiscussionReplyText.Trim().Length:N0} chars). Please shorten your message.";
            return;
        }

        IsSendingDiscussionReply = true;
        DiscussionSendError = null;

        try
        {
            var replyToId = ReplyToComment?.CommentId;
            var (success, error) = await _api.PostCommentAsync(
                DiscussionPostId, DiscussionReplyText.Trim(), ct, replyToId);

            if (success)
            {
                var indentLevel = ReplyToComment is not null ? ReplyToComment.IndentLevel + 1 : 0;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var newComment = new DiscussionComment
                    {
                        CommentId = Guid.NewGuid().ToString(),
                        Author = "OnTheEdgeOfReality",
                        Body = DiscussionReplyText.Trim(),
                        Timestamp = DateTimeOffset.Now,
                        IsVerified = false,
                        IndentLevel = indentLevel,
                        ReplyToId = replyToId
                    };

                    if (ReplyToComment is not null)
                    {
                        var parentIndex = DiscussionComments.IndexOf(ReplyToComment);
                        if (parentIndex >= 0)
                        {
                            int insertAt = parentIndex + 1;
                            while (insertAt < DiscussionComments.Count &&
                                   DiscussionComments[insertAt].IndentLevel > ReplyToComment.IndentLevel)
                            {
                                insertAt++;
                            }
                            DiscussionComments.Insert(insertAt, newComment);
                        }
                        else
                        {
                            DiscussionComments.Add(newComment);
                        }
                    }
                    else
                    {
                        DiscussionComments.Add(newComment);
                    }
                });

                DiscussionReplyText = string.Empty;
                ReplyToComment = null;
            }
            else
            {
                DiscussionSendError = error ?? "Failed to send reply";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Feed: send reply failed", ex);
            DiscussionSendError = ex.Message;
        }
        finally
        {
            IsSendingDiscussionReply = false;
        }
    }

    [RelayCommand]
    private async Task RefreshFeedDiscussionAsync(CancellationToken ct)
    {
        if (SelectedPost is not null)
            await LoadDiscussionAsync(SelectedPost, ct);
    }

    [RelayCommand]
    private void Subscribe()
    {
        if (DiscussionPostId is not null)
            SubscribeRequested?.Invoke(DiscussionPostId);
    }

    private void InsertSorted(ObservableCollection<ActivityItem> list, ActivityItem item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            bool before = SortByScore
                ? item.Score > list[i].Score
                : item.Timestamp >= list[i].Timestamp;
            if (before)
            {
                list.Insert(i, item);
                return;
            }
        }
        list.Add(item);
    }

    private static DateTimeOffset ParseTimestamp(string? ts)
    {
        if (ts is not null && DateTimeOffset.TryParse(ts, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }
}

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

/// <summary>Model for a watched discussion in the left panel.</summary>
public partial class WatchedDiscussionItem : ObservableObject
{
    [ObservableProperty] private string _postId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private int _commentCount;
    [ObservableProperty] private DateTimeOffset _lastActivity;
    [ObservableProperty] private int _newCommentCount;
}

public partial class CommentsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<ActivityItem> Comments { get; } = new();

    /// <summary>Left-panel list of watched discussions (Feature 1).</summary>
    public ObservableCollection<WatchedDiscussionItem> Discussions { get; } = new();

    [ObservableProperty] private ActivityItem? _selectedComment;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private string? _sendError;

    // ── Discussion Panel State (Feature 1) ─────────────────────
    [ObservableProperty] private WatchedDiscussionItem? _selectedDiscussion;
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

    /// <summary>Callback to unsubscribe from a post (wired by MainViewModel).</summary>
    public Action<string>? UnsubscribeRequested { get; set; }

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
            IsNew = isNew,
            MarkedNewAt = isNew ? DateTimeOffset.Now : default
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

    // ── Discussion Panel Methods (Feature 1) ──────────────────

    public async Task LoadDiscussionAsync(WatchedDiscussionItem discussion, CancellationToken ct)
    {
        SelectedDiscussion = discussion;
        DiscussionPostId = discussion.PostId;
        DiscussionTitle = discussion.Title;
        DiscussionBody = string.Empty;
        DiscussionAuthor = discussion.Author;
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
            var post = await _api.GetPostWithCommentsAsync(discussion.PostId, ct);
            if (post is null)
            {
                DiscussionBody = "(Failed to load post)";
                return;
            }

            DiscussionTitle = post.Title ?? post.Summary ?? "(untitled)";
            DiscussionBody = post.Body ?? post.Summary ?? "(no body)";
            DiscussionAuthor = post.Author ?? "unknown";

            var comments = post.Comments ?? [];
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

            // Reset new comment count for this discussion
            discussion.NewCommentCount = 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
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
        SelectedDiscussion = null;
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
    private void CancelDiscussionReplyTo()
    {
        ReplyToComment = null;
    }

    [RelayCommand]
    private async Task SendDiscussionReplyAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(DiscussionPostId) || string.IsNullOrWhiteSpace(DiscussionReplyText))
            return;

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
            DiscussionSendError = ex.Message;
        }
        finally
        {
            IsSendingDiscussionReply = false;
        }
    }

    [RelayCommand]
    private async Task RefreshDiscussionAsync(CancellationToken ct)
    {
        if (SelectedDiscussion is not null)
            await LoadDiscussionAsync(SelectedDiscussion, ct);
    }

    [RelayCommand]
    private void Unsubscribe()
    {
        if (DiscussionPostId is not null)
        {
            var postId = DiscussionPostId;
            CloseDiscussion();
            UnsubscribeRequested?.Invoke(postId);
        }
    }

    private static DateTimeOffset ParseTimestamp(string? ts)
    {
        if (ts is not null && DateTimeOffset.TryParse(ts, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }
}

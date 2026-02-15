using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

/// <summary>
/// Persistent state that tracks what we've already seen across app restarts.
/// Saved as JSON in the .gather directory.
/// </summary>
public class WhatsNewState
{
    public HashSet<string> SeenAgentIds { get; set; } = new();
    public HashSet<string> SeenSkillIds { get; set; } = new();
    public HashSet<string> SeenPlatformPostIds { get; set; } = new();
    public HashSet<string> SeenDigestPostIds { get; set; } = new();
    public string? LastFeeScheduleHash { get; set; }
    public string? LastOpenApiSpecHash { get; set; }
    public DateTimeOffset LastCheckTime { get; set; } = DateTimeOffset.MinValue;
}

/// <summary>
/// User preferences — persisted alongside state.
/// Covers What's New display limits and general app appearance.
/// </summary>
public class WhatsNewOptions
{
    public int MaxDigestPosts { get; set; } = 20;
    public int MaxPlatformPosts { get; set; } = 20;
    public int MaxAgents { get; set; } = 50;
    public int MaxSkills { get; set; } = 50;
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Font size scale as a percentage (100 = normal, 125 = 25% larger, 80 = 20% smaller).
    /// Applied as a LayoutTransform on the entire window content.
    /// </summary>
    public int FontScalePercent { get; set; } = 100;

    // ── Channels tab settings ────────────────────────────────────
    public int MaxChannelsTab { get; set; } = 50;
    public bool ShowAllChannels { get; set; }
    public List<string> SubscribedChannelIds { get; set; } = new();

    // ── Agents tab settings ──────────────────────────────────────
    public int MaxAgentsTab { get; set; } = 50;

    // ── Post display ──────────────────────────────────────────────
    /// <summary>
    /// When true, request full post bodies from the API instead of summaries.
    /// Uses more API tokens but shows complete post content everywhere.
    /// </summary>
    public bool ShowFullPosts { get; set; }

    // ── Compose limits ──────────────────────────────────────────────
    /// <summary>
    /// Maximum character length for comments and channel messages.
    /// The Gather API enforces a server-side limit of 2,000 characters.
    /// </summary>
    public int MaxCommentLength { get; set; } = 2000;

    // ── Log settings ──────────────────────────────────────────────
    /// <summary>
    /// Maximum size of each polling log file in kilobytes.
    /// When exceeded, files are rotated (01→02, etc., up to 10).
    /// </summary>
    public int MaxLogSizeKB { get; set; } = 256;
}

public partial class WhatsNewViewModel : ObservableObject
{
    private readonly GatherApiClient _api;
    private readonly string _stateFilePath;
    private readonly string _optionsFilePath;
    private WhatsNewState _state;
    private WhatsNewOptions _options;

    public ObservableCollection<WhatsNewEntry> Entries { get; } = new();

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _statusText = "Not yet checked";
    [ObservableProperty] private int _newCount;

    // ── Discussion Panel State ──────────────────────────────────
    [ObservableProperty] private WhatsNewEntry? _selectedEntry;
    [ObservableProperty] private string? _discussionPostId;
    [ObservableProperty] private string _discussionTitle = string.Empty;
    [ObservableProperty] private string _discussionBody = string.Empty;
    [ObservableProperty] private string _discussionAuthor = string.Empty;
    [ObservableProperty] private bool _isLoadingDiscussion;
    [ObservableProperty] private bool _hasDiscussion;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private bool _isSendingReply;
    [ObservableProperty] private string? _sendError;

    /// <summary>The comment we're replying to (null = top-level reply to post).</summary>
    [ObservableProperty] private DiscussionComment? _replyToComment;

    /// <summary>Whether the post body panel is expanded to show full text.</summary>
    [ObservableProperty] private bool _isPostBodyExpanded;

    /// <summary>Whether the reply TextBox is expanded (focused/active).</summary>
    [ObservableProperty] private bool _isReplyExpanded;

    public ObservableCollection<DiscussionComment> DiscussionComments { get; } = new();

    /// <summary>Expose options so the Options dialog can bind to them.</summary>
    public WhatsNewOptions Options => _options;

    public WhatsNewViewModel(GatherApiClient api, string keysDirectory)
    {
        _api = api;

        // Store state file alongside keys
        var stateDir = string.IsNullOrEmpty(keysDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gather")
            : keysDirectory;
        _stateFilePath = Path.Combine(stateDir, "whatsnew_state.json");
        _optionsFilePath = Path.Combine(stateDir, "whatsnew_options.json");

        _state = LoadState();
        _options = LoadOptions();
    }

    /// <summary>
    /// Called when the What's New tab becomes active. Queries all discovery
    /// endpoints and reports what has changed since the last check.
    /// Always shows ALL items from the API — only genuinely new ones get IsNew=true.
    /// </summary>
    [RelayCommand]
    public async Task CheckForNewsAsync(CancellationToken ct)
    {
        if (IsChecking) return;

        IsChecking = true;
        StatusText = "Checking for news...";
        int newItemCount = 0;

        try
        {
            AppLogger.Log("WhatsNew", "Starting What's New check...");

            // Clear previous entries to rebuild fresh
            Application.Current.Dispatcher.Invoke(() => Entries.Clear());

            // Run all discovery queries in parallel
            var digestTask = _api.GetDigestAsync(ct);
            var platformTask = _api.GetPlatformPostsAsync(ct);
            var skillsTask = _api.GetSkillsAsync(ct);
            var agentsTask = _api.GetAgentsAsync(ct);
            var feesTask = _api.GetFeeScheduleAsync(ct);
            var specTask = _api.GetOpenApiSpecAsync(ct);

            await Task.WhenAll(digestTask, platformTask, skillsTask, agentsTask, feesTask, specTask);

            var digest = digestTask.Result;
            var platform = platformTask.Result;
            var skills = skillsTask.Result;
            var agents = agentsTask.Result;
            var fees = feesTask.Result;
            var spec = specTask.Result;

            // ── Always show agents (mark new ones with badge) ──
            int newAgentCount = 0;
            if (agents?.Agents is not null)
            {
                var toShow = agents.Agents.Take(_options.MaxAgents);
                foreach (var agent in toShow)
                {
                    if (agent.Id is null) continue;
                    bool isNew = _state.SeenAgentIds.Add(agent.Id);
                    if (isNew) { newAgentCount++; newItemCount++; }
                    AddEntry(new WhatsNewEntry
                    {
                        Category = "New Agent",
                        Title = agent.Name ?? agent.Id,
                        Description = agent.Description ?? "(no description)",
                        Detail = $"Posts: {agent.PostCount} | Verified: {agent.Verified}",
                        Timestamp = ParseTimestamp(agent.Created),
                        IsNew = isNew,
                        MarkedNewAt = isNew ? DateTimeOffset.Now : default
                    });
                }
                AppLogger.Log("WhatsNew", $"Agents: {agents.Agents.Count} total, {newAgentCount} new");
            }

            // ── Always show skills ──
            int newSkillCount = 0;
            if (skills?.Skills is not null)
            {
                var toShow = skills.Skills.Take(_options.MaxSkills);
                foreach (var skill in toShow)
                {
                    if (skill.Id is null) continue;
                    bool isNew = _state.SeenSkillIds.Add(skill.Id);
                    if (isNew) { newSkillCount++; newItemCount++; }
                    AddEntry(new WhatsNewEntry
                    {
                        Category = "New Skill",
                        Title = skill.Name ?? skill.Id,
                        Description = skill.Description ?? "(no description)",
                        Detail = $"By: {skill.Author ?? skill.AuthorId ?? "unknown"} | v{skill.Version ?? "?"} | Installs: {skill.InstallCount}",
                        Timestamp = ParseTimestamp(skill.Created),
                        IsNew = isNew,
                        MarkedNewAt = isNew ? DateTimeOffset.Now : default
                    });
                }
                AppLogger.Log("WhatsNew", $"Skills: {skills.Skills.Count} total, {newSkillCount} new");
            }

            // ── Always show platform announcements ──
            int newPlatformCount = 0;
            if (platform?.Posts is not null)
            {
                var toShow = platform.Posts.Take(_options.MaxPlatformPosts);
                foreach (var post in toShow)
                {
                    if (post.Id is null) continue;
                    bool isNew = _state.SeenPlatformPostIds.Add(post.Id);
                    if (isNew) { newPlatformCount++; newItemCount++; }
                    AddEntry(new WhatsNewEntry
                    {
                        Category = "Platform Announcement",
                        Title = post.Title ?? post.Summary ?? "(untitled)",
                        Description = FormatBody(post.Body ?? post.Summary ?? ""),
                        Detail = $"By: {post.Author ?? "platform"} | Score: {post.Score}",
                        Timestamp = ParseTimestamp(post.Created),
                        IsNew = isNew,
                        PostId = post.Id,
                        MarkedNewAt = isNew ? DateTimeOffset.Now : default
                    });
                }
                AppLogger.Log("WhatsNew", $"Platform posts: {platform.Posts.Count} total, {newPlatformCount} new");
            }

            // ── Always show daily digest trending posts ──
            int newDigestCount = 0;
            if (digest?.Posts is not null)
            {
                var toShow = digest.Posts.Take(_options.MaxDigestPosts);
                foreach (var post in toShow)
                {
                    if (post.Id is null) continue;
                    bool isNew = _state.SeenDigestPostIds.Add(post.Id);
                    if (isNew) { newDigestCount++; newItemCount++; }
                    AddEntry(new WhatsNewEntry
                    {
                        Category = "Trending Post",
                        Title = post.Title ?? post.Summary ?? "(untitled)",
                        Description = FormatBody(post.Summary ?? ""),
                        Detail = $"By: {post.Author ?? "unknown"} | Score: {post.Score} | Comments: {post.CommentCount} | Weight: {post.Weight:F2}",
                        Timestamp = ParseTimestamp(post.Created),
                        IsNew = isNew,
                        PostId = post.Id,
                        MarkedNewAt = isNew ? DateTimeOffset.Now : default
                    });
                }
                AppLogger.Log("WhatsNew", $"Digest posts: {digest.Posts.Count} total, {newDigestCount} new");
            }

            // ── Check fee schedule changes ──
            if (fees is not null)
            {
                var feeHash = ComputeSimpleHash($"{fees.PostFeeBch}|{fees.CommentFeeBch}|{fees.ChannelMessageFeeBch}|{fees.FreePostsPerWeek}|{fees.FreeCommentsPerDay}|{fees.FreeChannelMessagesPerDay}|{fees.PowDifficulty}");
                bool feeChanged = _state.LastFeeScheduleHash is not null && _state.LastFeeScheduleHash != feeHash;
                if (feeChanged) newItemCount++;

                AddEntry(new WhatsNewEntry
                {
                    Category = feeChanged ? "Fee Schedule Changed" : "Fee Schedule",
                    Title = feeChanged ? "Fee schedule has been updated" : "Current fee schedule",
                    Description = $"Post: {fees.PostFeeBch} BCH | Comment: {fees.CommentFeeBch} BCH | Channel msg: {fees.ChannelMessageFeeBch} BCH",
                    Detail = $"Free: {fees.FreePostsPerWeek} posts/wk, {fees.FreeCommentsPerDay} comments/day, {fees.FreeChannelMessagesPerDay} channel msgs/day | PoW: {fees.PowDifficulty}",
                    Timestamp = DateTimeOffset.Now,
                    IsNew = feeChanged
                });

                _state.LastFeeScheduleHash = feeHash;
            }

            // ── Check OpenAPI spec changes ──
            if (spec is not null)
            {
                var specHash = ComputeSimpleHash(spec);
                bool specChanged = _state.LastOpenApiSpecHash is not null && _state.LastOpenApiSpecHash != specHash;
                if (specChanged) newItemCount++;

                AddEntry(new WhatsNewEntry
                {
                    Category = specChanged ? "API Spec Changed" : "API Spec",
                    Title = specChanged
                        ? "The Gather OpenAPI specification has been updated"
                        : "OpenAPI spec baseline captured",
                    Description = specChanged
                        ? "The API may have new or modified endpoints. Check /openapi.json for details."
                        : "Future changes to the API specification will be detected.",
                    Detail = $"Spec size: {spec.Length:N0} chars",
                    Timestamp = DateTimeOffset.Now,
                    IsNew = specChanged
                });

                _state.LastOpenApiSpecHash = specHash;
            }

            // If ShowFullPosts is enabled, fetch full bodies for post entries
            if (_options.ShowFullPosts)
            {
                var postEntries = Entries.Where(e => !string.IsNullOrEmpty(e.PostId)).ToList();
                foreach (var entry in postEntries)
                {
                    try
                    {
                        var fullPost = await _api.GetPostWithCommentsAsync(entry.PostId!, ct);
                        if (fullPost?.Body is not null)
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                entry.Description = fullPost.Body;
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"WhatsNew: failed to fetch full body for {entry.PostId}", ex);
                    }
                }
                AppLogger.Log("WhatsNew", $"Fetched full bodies for {postEntries.Count} post entries");
            }

            // Update state
            _state.LastCheckTime = DateTimeOffset.Now;
            SaveState();

            NewCount = newItemCount;
            StatusText = newItemCount > 0
                ? $"Found {newItemCount} new item(s) — last check: {DateTimeOffset.Now.ToLocalTime():HH:mm:ss}"
                : $"No new items — last check: {DateTimeOffset.Now.ToLocalTime():HH:mm:ss}";

            AppLogger.Log("WhatsNew", $"Check complete: {newItemCount} new items, {Entries.Count} total entries");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: check failed", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    public void ResetNewCount() => NewCount = 0;

    // ── Discussion Panel Methods ────────────────────────────────

    /// <summary>Load discussion for a post when user clicks a trending/platform entry.</summary>
    public async Task LoadDiscussionAsync(WhatsNewEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entry.PostId)) return;

        SelectedEntry = entry;
        DiscussionPostId = entry.PostId;
        DiscussionTitle = entry.Title;
        DiscussionBody = string.Empty;
        DiscussionAuthor = string.Empty;
        IsLoadingDiscussion = true;
        HasDiscussion = true;
        SendError = null;
        ReplyText = string.Empty;
        ReplyToComment = null;
        IsPostBodyExpanded = false;
        IsReplyExpanded = false;

        Application.Current.Dispatcher.Invoke(() => DiscussionComments.Clear());

        try
        {
            AppLogger.Log("WhatsNew", $"Loading discussion for post {entry.PostId}...");
            var post = await _api.GetPostWithCommentsAsync(entry.PostId, ct);

            if (post is null)
            {
                AppLogger.Log("WhatsNew", "Post not found or failed to load");
                DiscussionBody = "(Failed to load post)";
                return;
            }

            DiscussionTitle = post.Title ?? post.Summary ?? "(untitled)";
            DiscussionBody = post.Body ?? post.Summary ?? "(no body)";
            DiscussionAuthor = post.Author ?? "unknown";

            var comments = post.Comments ?? [];
            AppLogger.Log("WhatsNew", $"Loaded {comments.Count} comments");

            // Build comment tree: top-level comments first, then nested replies
            var commentMap = comments.Where(c => c.Id is not null).ToDictionary(c => c.Id!);
            var topLevel = comments.Where(c => string.IsNullOrEmpty(c.ReplyTo)).ToList();
            var replies = comments.Where(c => !string.IsNullOrEmpty(c.ReplyTo)).ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Add top-level comments (oldest first for reading order)
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

                    // Add nested replies to this comment
                    AddReplies(c.Id!, replies, commentMap, 1);
                }
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: failed to load discussion", ex);
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

            // Recursively add replies to this reply
            if (reply.Id is not null)
                AddReplies(reply.Id, allReplies, commentMap, depth + 1);
        }
    }

    public void CloseDiscussion()
    {
        HasDiscussion = false;
        SelectedEntry = null;
        DiscussionPostId = null;
        ReplyToComment = null;
        Application.Current.Dispatcher.Invoke(() => DiscussionComments.Clear());
    }

    /// <summary>Set the comment to reply to (for threaded replies). Null = reply to post.</summary>
    public void SetReplyTo(DiscussionComment? comment)
    {
        ReplyToComment = comment;
        SendError = null;
    }

    /// <summary>Clear the reply-to target (reply to post instead).</summary>
    [RelayCommand]
    private void CancelReplyTo()
    {
        ReplyToComment = null;
    }

    [RelayCommand]
    private async Task SendReplyAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(DiscussionPostId) || string.IsNullOrWhiteSpace(ReplyText))
            return;

        if (ReplyText.Trim().Length > Converters.CharLimitSettings.MaxLength)
        {
            SendError = $"Comment exceeds {Converters.CharLimitSettings.MaxLength:N0} character limit ({ReplyText.Trim().Length:N0} chars). Please shorten your message.";
            return;
        }

        IsSendingReply = true;
        SendError = null;

        try
        {
            var replyToId = ReplyToComment?.CommentId;
            var (success, error) = await _api.PostCommentAsync(
                DiscussionPostId, ReplyText.Trim(), ct, replyToId);

            if (success)
            {
                AppLogger.Log("WhatsNew", $"Reply posted to {DiscussionPostId}" +
                    (replyToId is not null ? $" (reply to comment {replyToId})" : ""));

                var indentLevel = ReplyToComment is not null ? ReplyToComment.IndentLevel + 1 : 0;

                // Add optimistically to local list
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var newComment = new DiscussionComment
                    {
                        CommentId = Guid.NewGuid().ToString(),
                        Author = "OnTheEdgeOfReality",
                        Body = ReplyText.Trim(),
                        Timestamp = DateTimeOffset.Now,
                        IsVerified = false,
                        IndentLevel = indentLevel,
                        ReplyToId = replyToId
                    };

                    if (ReplyToComment is not null)
                    {
                        // Insert right after the parent comment and its existing replies
                        var parentIndex = DiscussionComments.IndexOf(ReplyToComment);
                        if (parentIndex >= 0)
                        {
                            // Find the insertion point: after all existing children of this parent
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

                ReplyText = string.Empty;
                ReplyToComment = null;
            }
            else
            {
                SendError = error ?? "Failed to send reply";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: send reply failed", ex);
            SendError = ex.Message;
        }
        finally
        {
            IsSendingReply = false;
        }
    }

    [RelayCommand]
    private async Task RefreshDiscussionAsync(CancellationToken ct)
    {
        if (SelectedEntry is not null)
            await LoadDiscussionAsync(SelectedEntry, ct);
    }

    private void AddEntry(WhatsNewEntry entry)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Insert newest first
            for (int i = 0; i < Entries.Count; i++)
            {
                if (entry.Timestamp >= Entries[i].Timestamp)
                {
                    Entries.Insert(i, entry);
                    return;
                }
            }
            Entries.Add(entry);
        });
    }

    // ── Options Persistence ──────────────────────────────────────

    public void SaveOptions()
    {
        try
        {
            var dir = Path.GetDirectoryName(_optionsFilePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_options, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_optionsFilePath, json);
            AppLogger.Log("WhatsNew", "Options saved");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: failed to save options", ex);
        }
    }

    private WhatsNewOptions LoadOptions()
    {
        try
        {
            if (File.Exists(_optionsFilePath))
            {
                var json = File.ReadAllText(_optionsFilePath);
                var opts = JsonSerializer.Deserialize<WhatsNewOptions>(json);
                if (opts is not null)
                {
                    AppLogger.Log("WhatsNew", $"Loaded options: MaxDigest={opts.MaxDigestPosts}, MaxPlatform={opts.MaxPlatformPosts}");
                    return opts;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: failed to load options", ex);
        }
        return new WhatsNewOptions();
    }

    // ── State Persistence ────────────────────────────────────────

    private WhatsNewState LoadState()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<WhatsNewState>(json);
                if (state is not null)
                {
                    AppLogger.Log("WhatsNew", $"Loaded state: {state.SeenAgentIds.Count} agents, {state.SeenSkillIds.Count} skills, {state.SeenPlatformPostIds.Count} platform posts, {state.SeenDigestPostIds.Count} digest posts");
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: failed to load state", ex);
        }
        return new WhatsNewState();
    }

    private void SaveState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
            AppLogger.Log("WhatsNew", "State saved");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WhatsNew: failed to save state", ex);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private string FormatBody(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        if (_options.ShowFullPosts) return text;
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }

    private static string ComputeSimpleHash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16]; // first 16 hex chars is plenty for change detection
    }

    private static DateTimeOffset ParseTimestamp(string? ts)
    {
        if (ts is not null && DateTimeOffset.TryParse(ts, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }
}

/// <summary>A single entry in the What's New log.</summary>
public partial class WhatsNewEntry : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private DateTimeOffset _timestamp;
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Gather post ID — set for Trending Post and Platform Announcement entries.</summary>
    [ObservableProperty] private string? _postId;

    /// <summary>When this entry was marked as new (for badge timeout).</summary>
    [ObservableProperty] private DateTimeOffset _markedNewAt;

    /// <summary>True if this entry has a discussion (clickable to open).</summary>
    public bool HasPost => !string.IsNullOrEmpty(PostId);
}

/// <summary>A comment in a post discussion, with threading support.</summary>
public partial class DiscussionComment : ObservableObject
{
    [ObservableProperty] private string _commentId = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private DateTimeOffset _timestamp;
    [ObservableProperty] private bool _isVerified;
    [ObservableProperty] private int _indentLevel;
    [ObservableProperty] private string? _replyToId;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Left margin based on indent level for visual threading.</summary>
    public double IndentMargin => IndentLevel * 24.0;
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

/// <summary>Lightweight wrapper for displaying a post in the agent's posts list.</summary>
public partial class AgentPostItem : ObservableObject
{
    [ObservableProperty] private string _postId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _commentCount;
    [ObservableProperty] private DateTimeOffset _timestamp;
}

public partial class AgentsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<AgentItem> Agents { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Not yet loaded";
    [ObservableProperty] private string _sortColumn = "PostCount";
    [ObservableProperty] private bool _sortDescending = true;
    [ObservableProperty] private AgentItem? _selectedAgent;

    // ── Agent Posts panel ─────────────────────────────────────────
    public ObservableCollection<AgentPostItem> AgentPosts { get; } = new();
    [ObservableProperty] private bool _isLoadingPosts;
    [ObservableProperty] private bool _hasAgentPosts;
    [ObservableProperty] private string _agentPostsStatus = string.Empty;

    // ── Start Discussion compose state ───────────────────────────
    [ObservableProperty] private bool _isComposing;
    [ObservableProperty] private string _newPostTitle = string.Empty;
    [ObservableProperty] private string _newPostBody = string.Empty;
    [ObservableProperty] private string _newPostTags = string.Empty;
    [ObservableProperty] private bool _isPosting;
    [ObservableProperty] private string? _postError;

    /// <summary>Callback invoked after a post is successfully created, with the post ID.</summary>
    public Action<string>? PostCreated { get; set; }

    /// <summary>Callback to navigate to a post discussion (in What's New tab).</summary>
    public Action<string>? NavigateToPost { get; set; }

    private int _maxAgents = 50;
    public int MaxAgents
    {
        get => _maxAgents;
        set => SetProperty(ref _maxAgents, value);
    }

    public AgentsViewModel(GatherApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedAgentChanged(AgentItem? value)
    {
        if (value is not null)
            _ = LoadAgentPostsAsync(value, CancellationToken.None);
        else
            CloseAgentPosts();
    }

    public async Task LoadAgentsAsync(CancellationToken ct)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Loading agents...";

        try
        {
            var response = await _api.GetAgentsAsync(ct);
            if (response?.Agents is null)
            {
                StatusText = "No agents found";
                return;
            }

            var sorted = ApplySort(response.Agents);
            var toShow = sorted.Take(MaxAgents).ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Agents.Clear();
                foreach (var agent in toShow)
                    Agents.Add(agent);
            });

            StatusText = $"{toShow.Count} of {response.Agents.Count} agents";
            AppLogger.Log("Agents", $"Loaded {toShow.Count} agents (max={MaxAgents})");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("Agents: load failed", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Agent Posts ───────────────────────────────────────────────

    private async Task LoadAgentPostsAsync(AgentItem agent, CancellationToken ct)
    {
        IsLoadingPosts = true;
        HasAgentPosts = true;
        AgentPostsStatus = $"Loading posts by {agent.Name}...";

        Application.Current.Dispatcher.Invoke(() => AgentPosts.Clear());

        try
        {
            var posts = await _api.GetPostsByAgentAsync(agent.Name ?? "", agent.Id, MaxAgents, ct);

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var p in posts)
                {
                    var bodyText = _api.ShowFullPosts
                        ? (p.Body ?? p.Summary ?? "")
                        : (p.Summary ?? p.Body ?? "");
                    AgentPosts.Add(new AgentPostItem
                    {
                        PostId = p.Id ?? "",
                        Title = p.Title ?? p.Summary ?? "(untitled)",
                        Summary = _api.ShowFullPosts ? bodyText : TruncateBody(bodyText, 120),
                        Score = p.Score,
                        CommentCount = p.CommentCount,
                        Timestamp = ParseTimestamp(p.Created)
                    });
                }
            });

            AgentPostsStatus = posts.Count > 0
                ? $"{posts.Count} post(s) by {agent.Name}"
                : $"No posts found for {agent.Name}";

            AppLogger.Log("Agents", $"Loaded {posts.Count} posts for agent {agent.Name}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError($"Agents: failed to load posts for {agent.Name}", ex);
            AgentPostsStatus = $"Error loading posts: {ex.Message}";
        }
        finally
        {
            IsLoadingPosts = false;
        }
    }

    /// <summary>Re-loads posts for the currently selected agent (e.g. after ShowFullPosts changes).</summary>
    public async Task ReloadAgentPostsAsync(CancellationToken ct)
    {
        if (SelectedAgent is not null)
            await LoadAgentPostsAsync(SelectedAgent, ct);
    }

    public void CloseAgentPosts()
    {
        HasAgentPosts = false;
        AgentPostsStatus = string.Empty;
        Application.Current?.Dispatcher?.Invoke(() => AgentPosts.Clear());
    }

    public void OpenPost(string postId)
    {
        NavigateToPost?.Invoke(postId);
    }

    // ── Sorting ──────────────────────────────────────────────────

    public void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column is "PostCount";
        }

        var list = Agents.ToList();
        var sorted = ApplySort(list).ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            Agents.Clear();
            foreach (var agent in sorted)
                Agents.Add(agent);
        });
    }

    private IEnumerable<AgentItem> ApplySort(IEnumerable<AgentItem> agents)
    {
        return SortColumn switch
        {
            "Name" => SortDescending
                ? agents.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : agents.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            "PostCount" => SortDescending
                ? agents.OrderByDescending(a => a.PostCount)
                : agents.OrderBy(a => a.PostCount),
            "Verified" => SortDescending
                ? agents.OrderByDescending(a => a.Verified).ThenByDescending(a => a.PostCount)
                : agents.OrderBy(a => a.Verified).ThenByDescending(a => a.PostCount),
            "Description" => SortDescending
                ? agents.OrderByDescending(a => a.Description, StringComparer.OrdinalIgnoreCase)
                : agents.OrderBy(a => a.Description, StringComparer.OrdinalIgnoreCase),
            _ => agents.OrderByDescending(a => a.PostCount)
        };
    }

    public string GetSortIndicator(string column)
    {
        if (SortColumn != column) return "";
        return SortDescending ? " \u25BC" : " \u25B2";
    }

    // ── Start Discussion ─────────────────────────────────────────

    [RelayCommand]
    private void StartDiscussion()
    {
        if (SelectedAgent is null) return;

        IsComposing = true;
        PostError = null;
        NewPostTitle = $"Discussion with @{SelectedAgent.Name}";
        NewPostBody = $"@{SelectedAgent.Name} ";
        NewPostTags = "discussion";
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
            PostError = "At least one tag is required";
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
                AppLogger.Log("Agents", $"Discussion created: \"{NewPostTitle.Trim()}\"");

                NewPostTitle = string.Empty;
                NewPostBody = string.Empty;
                NewPostTags = string.Empty;
                IsComposing = false;

                if (postId is not null)
                    PostCreated?.Invoke(postId);
            }
            else
            {
                PostError = error ?? "Failed to create post";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Agents: create discussion failed", ex);
            PostError = ex.Message;
        }
        finally
        {
            IsPosting = false;
        }
    }

    // ── Tipping (Task #8) ───────────────────────────────────────

    [ObservableProperty] private bool _showTipForm;
    [ObservableProperty] private string _tipAmount = string.Empty;
    [ObservableProperty] private string _tipMessage = string.Empty;
    [ObservableProperty] private bool _isTipping;
    [ObservableProperty] private string? _tipError;
    [ObservableProperty] private string? _tipSuccess;

    /// <summary>BCH to USD exchange rate, derived from the account balance.</summary>
    public decimal BchToUsdRate { get; set; }

    public string TipUsdEstimate
    {
        get
        {
            if (decimal.TryParse(TipAmount, out var bch) && bch > 0 && BchToUsdRate > 0)
                return $"~${bch * BchToUsdRate:F2} USD";
            return "";
        }
    }

    partial void OnTipAmountChanged(string value)
    {
        OnPropertyChanged(nameof(TipUsdEstimate));
    }

    [RelayCommand]
    private void ShowTip()
    {
        if (SelectedAgent is null) return;
        ShowTipForm = true;
        TipError = null;
        TipSuccess = null;
        TipAmount = string.Empty;
        TipMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelTip()
    {
        ShowTipForm = false;
        TipAmount = string.Empty;
        TipMessage = string.Empty;
        TipError = null;
        TipSuccess = null;
    }

    [RelayCommand]
    private async Task SendTipAsync(CancellationToken ct)
    {
        if (SelectedAgent?.Id is null)
        {
            TipError = "No agent selected";
            return;
        }

        if (!decimal.TryParse(TipAmount, out var amount) || amount <= 0)
        {
            TipError = "Enter a valid BCH amount (e.g. 0.001)";
            return;
        }

        IsTipping = true;
        TipError = null;
        TipSuccess = null;

        try
        {
            var msg = string.IsNullOrWhiteSpace(TipMessage) ? null : TipMessage.Trim();
            var (success, result, error) = await _api.TipAgentAsync(SelectedAgent.Id, amount, msg, ct);
            if (success)
            {
                TipSuccess = $"Tipped {amount} BCH to {SelectedAgent.Name}!";
                TipAmount = string.Empty;
                TipMessage = string.Empty;
                AppLogger.Log("Agents", $"Tipped {amount} BCH to {SelectedAgent.Name}");
            }
            else
            {
                TipError = error ?? "Failed to send tip";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Agents: tip failed", ex);
            TipError = ex.Message;
        }
        finally
        {
            IsTipping = false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string TruncateBody(string text, int maxLength = 120)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }

    private static DateTimeOffset ParseTimestamp(string? ts)
    {
        if (ts is not null && DateTimeOffset.TryParse(ts, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }
}

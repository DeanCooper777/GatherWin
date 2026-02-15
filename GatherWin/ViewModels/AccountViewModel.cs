using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    [ObservableProperty] private string? _balanceBch;
    [ObservableProperty] private string? _balanceUsd;
    [ObservableProperty] private int _postsAvailable;
    [ObservableProperty] private int _commentsAvailable;
    [ObservableProperty] private int _freePostsRemaining;
    [ObservableProperty] private int _freeCommentsRemaining;
    [ObservableProperty] private bool _isSuspended;
    [ObservableProperty] private DateTimeOffset _tokenExpiry;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string? _depositAddress;
    [ObservableProperty] private bool _showDeposit;

    public AccountViewModel(GatherApiClient api)
    {
        _api = api;
    }

    public ObservableCollection<WatchedPostInfo> WatchedPosts { get; } = new();

    public void UpdateFromBalance(BalanceResponse balance)
    {
        BalanceBch = balance.BalanceBch;
        BalanceUsd = balance.BalanceUsdApprox;
        FreePostsRemaining = balance.FreePostsRemainingThisWeek;
        FreeCommentsRemaining = balance.FreeCommentsRemaining;
        IsSuspended = balance.Suspended;

        var hasBch = decimal.TryParse(balance.BalanceBch, out var bchAmount);
        var hasPostFee = decimal.TryParse(balance.PostingFeeBch, out var postFee);
        var hasCommentFee = decimal.TryParse(balance.CommentFeeBch, out var commentFee);

        PostsAvailable = hasBch && hasPostFee && postFee > 0 ? (int)(bchAmount / postFee) : 0;
        CommentsAvailable = hasBch && hasCommentFee && commentFee > 0 ? (int)(bchAmount / commentFee) : 0;
    }

    // ── BCH Deposit (Task #9) ─────────────────────────────────

    [RelayCommand]
    private void ToggleDeposit() => ShowDeposit = !ShowDeposit;

    // ── Platform Feedback (Task #16) ───────────────────────────

    [ObservableProperty] private bool _showFeedback;
    [ObservableProperty] private int _feedbackRating = 3;
    [ObservableProperty] private string _feedbackMessage = string.Empty;
    [ObservableProperty] private bool _isSendingFeedback;
    [ObservableProperty] private string? _feedbackError;
    [ObservableProperty] private string? _feedbackSuccess;

    [RelayCommand]
    private void ToggleFeedback()
    {
        ShowFeedback = !ShowFeedback;
        FeedbackError = null;
        FeedbackSuccess = null;
    }

    [RelayCommand]
    private async Task SendFeedbackAsync(CancellationToken ct)
    {
        if (FeedbackRating < 1 || FeedbackRating > 5)
        {
            FeedbackError = "Rating must be 1-5";
            return;
        }

        IsSendingFeedback = true;
        FeedbackError = null;
        FeedbackSuccess = null;

        try
        {
            var msg = string.IsNullOrWhiteSpace(FeedbackMessage) ? null : FeedbackMessage.Trim();
            var (success, error) = await _api.SubmitFeedbackAsync(FeedbackRating, msg, ct);
            if (success)
            {
                FeedbackSuccess = "Feedback sent! Thank you.";
                FeedbackMessage = string.Empty;
                FeedbackRating = 3;
                AppLogger.Log("Account", $"Feedback submitted (rating={FeedbackRating})");
            }
            else
            {
                FeedbackError = error ?? "Failed to send feedback";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Account: feedback failed", ex);
            FeedbackError = ex.Message;
        }
        finally
        {
            IsSendingFeedback = false;
        }
    }
}

public partial class WatchedPostInfo : ObservableObject
{
    [ObservableProperty] private string _postId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _commentCount;
    [ObservableProperty] private bool _isVerified;
}

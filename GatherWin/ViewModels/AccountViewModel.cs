using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GatherWin.Models;

namespace GatherWin.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    [ObservableProperty] private string? _balanceBch;
    [ObservableProperty] private string? _balanceUsd;
    [ObservableProperty] private int _postsAvailable;
    [ObservableProperty] private int _commentsAvailable;
    [ObservableProperty] private int _freePostsRemaining;
    [ObservableProperty] private int _freeCommentsRemaining;
    [ObservableProperty] private bool _isSuspended;
    [ObservableProperty] private DateTimeOffset _tokenExpiry;
    [ObservableProperty] private bool _isAuthenticated;

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
}

public partial class WatchedPostInfo : ObservableObject
{
    [ObservableProperty] private string _postId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _commentCount;
    [ObservableProperty] private bool _isVerified;
}

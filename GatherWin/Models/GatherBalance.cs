namespace GatherWin.Models;

public class BalanceResponse
{
    public string? BalanceBch { get; set; }
    public string? BalanceUsdApprox { get; set; }
    public string? PostingFeeBch { get; set; }
    public string? CommentFeeBch { get; set; }
    public int FreeCommentsRemaining { get; set; }
    public int FreePostsRemainingThisWeek { get; set; }
    public bool Suspended { get; set; }
}

public class FeesResponse
{
    public string? DepositAddress { get; set; }
    public string? PostFeeBch { get; set; }
    public string? CommentFeeBch { get; set; }
    public int PostFreeWeekly { get; set; }
    public int CommentFreeDaily { get; set; }
    public string? PostFeeUsd { get; set; }
    public string? CommentFeeUsd { get; set; }
}

public class TipResponse
{
    public string? FromBalanceBch { get; set; }
    public string? ToBalanceBch { get; set; }
    public string? AmountBch { get; set; }
    public string? Message { get; set; }
}

public class DepositResponse
{
    public string? AmountBch { get; set; }
    public string? NewBalanceBch { get; set; }
    public string? Message { get; set; }
}

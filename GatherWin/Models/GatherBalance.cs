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

public class TipResponse
{
    public decimal FromBalanceBch { get; set; }
    public decimal ToBalanceBch { get; set; }
    public decimal AmountBch { get; set; }
    public string? Message { get; set; }
}

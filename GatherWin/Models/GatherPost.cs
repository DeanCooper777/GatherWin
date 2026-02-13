namespace GatherWin.Models;

public class GatherPost
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Body { get; set; }
    public string? Author { get; set; }
    public string? AuthorId { get; set; }
    public bool Verified { get; set; }
    public int Score { get; set; }
    public int CommentCount { get; set; }
    public List<string>? Tags { get; set; }
    public string? Created { get; set; }
    public List<GatherComment>? Comments { get; set; }
}

public class GatherComment
{
    public string? Id { get; set; }
    public string? Author { get; set; }
    public string? AuthorId { get; set; }
    public bool Verified { get; set; }
    public string? Body { get; set; }
    public string? Created { get; set; }
    public string? ReplyTo { get; set; }
}

public class FeedResponse
{
    public List<GatherPost>? Posts { get; set; }
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}

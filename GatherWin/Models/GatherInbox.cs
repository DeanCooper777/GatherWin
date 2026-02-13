namespace GatherWin.Models;

public class InboxResponse
{
    public List<InboxMessage>? Messages { get; set; }
    public int Total { get; set; }
    public int Unread { get; set; }
}

public class InboxMessage
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool Read { get; set; }
    public string? Created { get; set; }

    // Reference fields — present when notification relates to a post/comment
    public string? PostId { get; set; }
    public string? CommentId { get; set; }
    public string? ChannelId { get; set; }
}

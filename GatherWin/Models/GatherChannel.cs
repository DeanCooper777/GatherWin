namespace GatherWin.Models;

public class ChannelListResponse
{
    public List<ChannelItem>? Channels { get; set; }
    public int Total { get; set; }
}

public class ChannelItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CreatorId { get; set; }
    public int MemberCount { get; set; }
    public string? Created { get; set; }
}

public class ChannelMessageListResponse
{
    public List<ChannelMessage>? Messages { get; set; }
    public int Total { get; set; }
}

public class ChannelMessage
{
    public string? Id { get; set; }
    public string? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? Body { get; set; }
    public string? Created { get; set; }
    public string? ReplyTo { get; set; }
}

namespace GatherWin.Models;

public class EmailItem
{
    public string? Id { get; set; }
    public string? Direction { get; set; }
    public string? FromAddr { get; set; }
    public string? ToAddr { get; set; }
    public string? Subject { get; set; }
    public string? BodyText { get; set; }
    public bool Read { get; set; }
    public string? Created { get; set; }
}

public class EmailDetail : EmailItem
{
    public string? BodyHtml { get; set; }
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
}

public class EmailListResponse
{
    public List<EmailItem>? Emails { get; set; }
    public int Total { get; set; }
    public int Unread { get; set; }
}

public class EmailSendRequest
{
    public string? To { get; set; }
    public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
    public string? InReplyTo { get; set; }
}

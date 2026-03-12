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

    /// <summary>Plain-text representation for display: uses BodyText if available,
    /// otherwise strips HTML tags from BodyHtml.</summary>
    public string BodyDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(BodyText)) return BodyText;
            if (string.IsNullOrEmpty(BodyHtml)) return string.Empty;
            // Replace block-level tags with newlines, then strip remaining tags
            var text = System.Text.RegularExpressions.Regex.Replace(BodyHtml, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"</p>|</div>|</li>|</tr>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", string.Empty);
            text = System.Net.WebUtility.HtmlDecode(text);
            return text.Trim();
        }
    }
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

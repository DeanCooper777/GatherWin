namespace GatherWin.Models;

public enum LogEntryType
{
    None,
    Comment,
    Inbox,
    FeedPost,
    Channel,
    Error
}

public class PollingLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public LogEntryType EntryType { get; set; }
}

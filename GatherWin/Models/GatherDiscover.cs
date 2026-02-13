namespace GatherWin.Models;

// ── Digest (top posts) ──────────────────────────────────────────

public class DigestResponse
{
    public List<DigestPost>? Posts { get; set; }
}

public class DigestPost
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Author { get; set; }
    public string? AuthorId { get; set; }
    public double Weight { get; set; }
    public int Score { get; set; }
    public int CommentCount { get; set; }
    public List<string>? Tags { get; set; }
    public string? Created { get; set; }
}

// ── Skills Marketplace ──────────────────────────────────────────

public class SkillsResponse
{
    public List<SkillItem>? Skills { get; set; }
    public int Total { get; set; }
}

public class SkillItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AuthorId { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Category { get; set; }
    public int InstallCount { get; set; }
    public string? Created { get; set; }
    public string? Updated { get; set; }
}

// ── Agent Directory ─────────────────────────────────────────────

public class AgentsResponse
{
    public List<AgentItem>? Agents { get; set; }
    public int Total { get; set; }
}

public class AgentItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool Verified { get; set; }
    public int PostCount { get; set; }
    public string? Created { get; set; }
}

// ── Fee Schedule ────────────────────────────────────────────────

public class FeeScheduleResponse
{
    public string? PostFeeBch { get; set; }
    public string? CommentFeeBch { get; set; }
    public string? ChannelMessageFeeBch { get; set; }
    public int FreePostsPerWeek { get; set; }
    public int FreeCommentsPerDay { get; set; }
    public int FreeChannelMessagesPerDay { get; set; }
    public string? PowDifficulty { get; set; }
}

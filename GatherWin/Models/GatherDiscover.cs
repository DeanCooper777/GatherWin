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

// ── Reviews ────────────────────────────────────────────────────

public class ReviewsResponse
{
    public List<ReviewItem>? Reviews { get; set; }
}

public class ReviewItem
{
    public string? Id { get; set; }
    public string? Skill { get; set; }
    public string? SkillName { get; set; }
    public string? AgentId { get; set; }
    public string? Task { get; set; }
    public string? Status { get; set; }
    public int? Score { get; set; }
    public int? SecurityScore { get; set; }
    public bool VerifiedReviewer { get; set; }
    public bool Challenged { get; set; }
    public string? RunnerType { get; set; }
    public string? WhatWorked { get; set; }
    public string? WhatFailed { get; set; }
    public string? SkillFeedback { get; set; }
    public string? Created { get; set; }
}

public class SubmitReviewResponse
{
    public string? Message { get; set; }
    public string? ReviewId { get; set; }
    public string? SkillId { get; set; }
    public int? Score { get; set; }
    public bool VerifiedReviewer { get; set; }
    public bool Challenged { get; set; }
}

// ── Shop / Menu ────────────────────────────────────────────────

public class MenuResponse
{
    public List<MenuCategory>? Categories { get; set; }
}

public class MenuCategory
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int Count { get; set; }
    public string? Href { get; set; }
}

public class CategoryItemsResponse
{
    public string? Category { get; set; }
    public List<MenuItem>? Items { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public string? Next { get; set; }
}

public class MenuItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public bool Available { get; set; }
    public string? BasePriceBch { get; set; }
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

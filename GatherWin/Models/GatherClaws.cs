namespace GatherWin.Models;

public class ClawItem
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? Instructions { get; set; }
    public string? GithubRepo { get; set; }
    public string? ClawType { get; set; }
    public string? AgentType { get; set; }
    public string? UserId { get; set; }
    public string? Subdomain { get; set; }
    public string? ContainerId { get; set; }
    public string? Url { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsPublic { get; set; }
    public int HeartbeatInterval { get; set; }
    public string? HeartbeatInstruction { get; set; }
    public bool Paid { get; set; }
    public string? TrialEndsAt { get; set; }
    public string? AgentId { get; set; }
    public string? LastHeartbeat { get; set; }
    public string? Created { get; set; }
}

public class ClawsListResponse
{
    public List<ClawItem>? Claws { get; set; }
    public int Total { get; set; }
}

public class ClawDeployRequest
{
    public string? Name { get; set; }
    public string? Instructions { get; set; }
    public string? GithubRepo { get; set; }
    public string? ClawType { get; set; }
    public string? AgentType { get; set; }
}

public class ClawUpdateRequest
{
    public bool? IsPublic { get; set; }
    public int? HeartbeatInterval { get; set; }
    public string? HeartbeatInstruction { get; set; }
}

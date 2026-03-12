namespace GatherWin.Models;

// ── Rankings (top skills by score) ─────────────────────────────

public class RankedSkill
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public double Installs { get; set; }
    public double ReviewCount { get; set; }
    public double? AvgScore { get; set; }
    public double? RankScore { get; set; }
    public int VerifiedProofs { get; set; }
}

public class RankingsResponse
{
    public List<RankedSkill>? Rankings { get; set; }
    public int Count { get; set; }
}

// ── Proofs (cryptographic review attestations) ─────────────────

public class ProofListItem
{
    public string? Id { get; set; }
    public string? ReviewId { get; set; }
    public string? SkillId { get; set; }
    public bool Verified { get; set; }
    public string? Created { get; set; }
}

public class ProofsListResponse
{
    public List<ProofListItem>? Proofs { get; set; }
}

public class ProofDetail
{
    public string? Id { get; set; }
    public string? ReviewId { get; set; }
    public string? SkillId { get; set; }
    public string? Task { get; set; }
    public bool Verified { get; set; }
    public string? Created { get; set; }
}

public class ProofVerifyResponse
{
    public string? Id { get; set; }
    public bool Verified { get; set; }
    public string? Message { get; set; }
}

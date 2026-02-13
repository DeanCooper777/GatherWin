namespace GatherWin.Models;

public class ChallengeRequest
{
    public string? PublicKey { get; set; }
}

public class ChallengeResponse
{
    public string? Nonce { get; set; }
    public long ExpiresIn { get; set; }
}

public class AuthenticateRequest
{
    public string? PublicKey { get; set; }
    public string? Signature { get; set; }
}

public class AuthenticateResponse
{
    public string? Token { get; set; }
    public string? AgentId { get; set; }
    public long ExpiresIn { get; set; }
    public long UnreadMessages { get; set; }
}

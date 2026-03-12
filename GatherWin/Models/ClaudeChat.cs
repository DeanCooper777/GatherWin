namespace GatherWin.Models;

/// <summary>
/// A named, persistent conversation with Claude.
/// Stored to disk as JSON; also used as the ViewModel data source.
/// </summary>
public class ClaudeConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Chat";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string SystemPrompt { get; set; } = string.Empty;
    public string Created { get; set; } = DateTime.Now.ToString("o");
    public List<ClaudeChatEntry> Messages { get; set; } = new();
}

/// <summary>
/// One turn in a conversation — stored on disk and sent to the API.
/// </summary>
public class ClaudeChatEntry
{
    public string Role { get; set; } = "user";     // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");
}

/// <summary>Model option for the ComboBox in the Claude tab.</summary>
public class ClaudeModelOption
{
    public string Id { get; }
    public string Label { get; }
    public ClaudeModelOption(string id, string label) { Id = id; Label = label; }
    public override string ToString() => Label;
}

/// <summary>
/// A named AI agent with its own system prompt, model, and description.
/// The description is shown to the orchestrator so it knows when to invoke the agent.
/// </summary>
public class ClaudeAgent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Agent";
    public string Description { get; set; } = string.Empty;   // used by orchestrator
    public string SystemPrompt { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
}

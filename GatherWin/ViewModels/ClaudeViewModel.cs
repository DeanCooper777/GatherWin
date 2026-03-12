using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class ClaudeViewModel : ObservableObject
{
    private readonly ClaudeApiClient _claude;

    private static readonly string ConversationsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GatherWin", "claude-conversations.json");

    private static readonly string AgentsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GatherWin", "claude-agents.json");

    // ── Left panel mode ───────────────────────────────────────────

    [ObservableProperty] private string _leftPanel = "chats";
    public bool IsShowingChats  => LeftPanel == "chats";
    public bool IsShowingAgents => LeftPanel == "agents";

    partial void OnLeftPanelChanged(string value)
    {
        OnPropertyChanged(nameof(IsShowingChats));
        OnPropertyChanged(nameof(IsShowingAgents));
    }

    [RelayCommand] private void ShowChats()  => LeftPanel = "chats";
    [RelayCommand] private void ShowAgents() => LeftPanel = "agents";

    // ── Conversation list ─────────────────────────────────────────

    public ObservableCollection<ClaudeConversation> Conversations { get; } = new();

    [ObservableProperty] private ClaudeConversation? _selectedConversation;
    [ObservableProperty] private string _statusText = "No conversation selected";
    [ObservableProperty] private bool _isConfigured;
    public bool IsNotConfigured => !IsConfigured;

    // ── Chat display ──────────────────────────────────────────────

    public ObservableCollection<ClawChatMessage> DisplayMessages { get; } = new();

    [ObservableProperty] private string _chatInput = string.Empty;
    [ObservableProperty] private bool _isSendingChat;
    [ObservableProperty] private string? _chatError;

    // ── System prompt panel ───────────────────────────────────────

    [ObservableProperty] private bool _showSystemPrompt;
    [ObservableProperty] private string _editingSystemPrompt = string.Empty;

    // ── Orchestrator mode ─────────────────────────────────────────

    [ObservableProperty] private bool _orchestratorMode;

    partial void OnOrchestratorModeChanged(bool value) => RefreshStatusText();

    // ── Agent registry ────────────────────────────────────────────

    public ObservableCollection<ClaudeAgent> Agents { get; } = new();

    [ObservableProperty] private ClaudeAgent? _selectedAgent;

    // Agent editor fields (synced from SelectedAgent)
    [ObservableProperty] private string _agentName = string.Empty;
    [ObservableProperty] private string _agentDescription = string.Empty;
    [ObservableProperty] private string _agentSystemPrompt = string.Empty;
    [ObservableProperty] private string _agentModel = "claude-sonnet-4-6";
    [ObservableProperty] private string? _agentSaveStatus;

    partial void OnSelectedAgentChanged(ClaudeAgent? value)
    {
        AgentSaveStatus = null;
        if (value is null) return;
        AgentName = value.Name;
        AgentDescription = value.Description;
        AgentSystemPrompt = value.SystemPrompt;
        AgentModel = value.Model;
    }

    // ── Model selector ────────────────────────────────────────────

    public static IReadOnlyList<ClaudeModelOption> Models { get; } = new[]
    {
        new ClaudeModelOption("claude-opus-4-6",           "Opus 4.6  (most capable)"),
        new ClaudeModelOption("claude-sonnet-4-6",         "Sonnet 4.6  (balanced)"),
        new ClaudeModelOption("claude-haiku-4-5-20251001", "Haiku 4.5  (fastest)"),
    };

    // ── Tool definitions for the orchestrator ─────────────────────

    private static readonly object[] OrchestratorTools = new object[]
    {
        new
        {
            name = "invoke_agent",
            description = "Delegate a specific task to a named specialized agent and receive their response.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    agent_name = new
                    {
                        type = "string",
                        description = "Exact name of the agent to invoke (must match an available agent name)"
                    },
                    task = new
                    {
                        type = "string",
                        description = "The complete task or question for the agent, with all necessary context"
                    }
                },
                required = new[] { "agent_name", "task" }
            }
        }
    };

    // ── Constructor ───────────────────────────────────────────────

    public ClaudeViewModel(string apiKey)
    {
        _claude = new ClaudeApiClient(apiKey);
        IsConfigured = _claude.IsConfigured;
        LoadConversations();
        LoadAgents();
        if (!IsConfigured)
            StatusText = "Add a Claude API key in Options to use this tab";
    }

    // ── Conversation selection ────────────────────────────────────

    partial void OnSelectedConversationChanged(ClaudeConversation? value)
    {
        DisplayMessages.Clear();
        ChatError = null;
        ShowSystemPrompt = false;

        if (value is null) { StatusText = "No conversation selected"; return; }

        EditingSystemPrompt = value.SystemPrompt;
        RebuildDisplayMessages(value);
        RefreshStatusText();
    }

    private void RefreshStatusText()
    {
        if (SelectedConversation is null) { StatusText = "No conversation selected"; return; }
        if (OrchestratorMode)
            StatusText = $"Orchestrator  ·  {Agents.Count} agent(s) available";
        else
            StatusText = $"{SelectedConversation.Model}  ·  {SelectedConversation.Messages.Count / 2} exchange(s)";
    }

    private void RebuildDisplayMessages(ClaudeConversation conv)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            DisplayMessages.Clear();
            foreach (var entry in conv.Messages)
                DisplayMessages.Add(EntryToDisplay(entry));
        });
    }

    private static ClawChatMessage EntryToDisplay(ClaudeChatEntry e) =>
        new() { Role = e.Role, Body = e.Content, Timestamp = e.Timestamp };

    // ── Conversation management ───────────────────────────────────

    [RelayCommand]
    private void NewConversation()
    {
        var conv = new ClaudeConversation { Model = Models[1].Id };
        Conversations.Insert(0, conv);
        SelectedConversation = conv;
        SaveConversations();
    }

    [RelayCommand]
    private void DeleteConversation()
    {
        if (SelectedConversation is null) return;
        var idx = Conversations.IndexOf(SelectedConversation);
        Conversations.Remove(SelectedConversation);
        SelectedConversation = Conversations.Count > 0 ? Conversations[Math.Max(0, idx - 1)] : null;
        SaveConversations();
    }

    [RelayCommand]
    private void ClearConversation()
    {
        if (SelectedConversation is null) return;
        SelectedConversation.Messages.Clear();
        DisplayMessages.Clear();
        RefreshStatusText();
        SaveConversations();
    }

    [RelayCommand]
    private void ToggleSystemPrompt()
    {
        ShowSystemPrompt = !ShowSystemPrompt;
        if (ShowSystemPrompt && SelectedConversation is not null)
            EditingSystemPrompt = SelectedConversation.SystemPrompt;
    }

    [RelayCommand]
    private void SaveSystemPrompt()
    {
        if (SelectedConversation is null) return;
        SelectedConversation.SystemPrompt = EditingSystemPrompt;
        ShowSystemPrompt = false;
        SaveConversations();
    }

    public void SetModel(string modelId)
    {
        if (SelectedConversation is null) return;
        SelectedConversation.Model = modelId;
        RefreshStatusText();
        SaveConversations();
    }

    // ── Agent management ──────────────────────────────────────────

    [RelayCommand]
    private void NewAgent()
    {
        var agent = new ClaudeAgent { Name = "New Agent", Description = "Describe what this agent specialises in" };
        Agents.Add(agent);
        SelectedAgent = agent;
        SaveAgents();
    }

    [RelayCommand]
    private void DeleteAgent()
    {
        if (SelectedAgent is null) return;
        var idx = Agents.IndexOf(SelectedAgent);
        Agents.Remove(SelectedAgent);
        SelectedAgent = Agents.Count > 0 ? Agents[Math.Max(0, idx - 1)] : null;
        SaveAgents();
    }

    [RelayCommand]
    private void SaveAgent()
    {
        if (SelectedAgent is null) return;
        SelectedAgent.Name         = AgentName.Trim();
        SelectedAgent.Description  = AgentDescription.Trim();
        SelectedAgent.SystemPrompt = AgentSystemPrompt.Trim();
        SelectedAgent.Model        = AgentModel;
        AgentSaveStatus = "Saved";

        // Refresh list so updated name appears — capture ref before removal
        // (removing an item clears SelectedAgent via the ListBox, so use local var)
        var agent = SelectedAgent;
        var idx = Agents.IndexOf(agent);
        if (idx >= 0) { Agents.RemoveAt(idx); Agents.Insert(idx, agent); }
        SelectedAgent = agent;  // re-select after re-insert

        SaveAgents();
        RefreshStatusText();
    }

    /// <summary>
    /// Create a new conversation pre-configured with this agent's system prompt and model,
    /// then switch to the Chats panel so the user can chat with it directly.
    /// </summary>
    [RelayCommand]
    private void ChatWithAgent()
    {
        if (SelectedAgent is null) return;
        var agent = SelectedAgent;
        var conv = new ClaudeConversation
        {
            Name         = $"Chat: {agent.Name}",
            Model        = agent.Model,
            SystemPrompt = agent.SystemPrompt
        };
        Conversations.Insert(0, conv);
        SelectedConversation = conv;
        LeftPanel = "chats";   // switch to Chats panel so user sees the conversation
        SaveConversations();
    }

    // ── Send message ──────────────────────────────────────────────

    [RelayCommand]
    private Task SendMessageAsync(CancellationToken ct) =>
        OrchestratorMode && Agents.Count > 0
            ? RunOrchestratedAsync(ChatInput.Trim(), ct)
            : RunDirectAsync(ChatInput.Trim(), ct);

    // Direct (no tools) ───────────────────────────────────────────

    private async Task RunDirectAsync(string msg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;
        if (SelectedConversation is null) { ChatError = "No conversation selected"; return; }
        if (!IsConfigured) { ChatError = "Claude API key not configured — add it in Options"; return; }

        ChatInput = string.Empty;
        IsSendingChat = true;
        ChatError = null;

        if (SelectedConversation.Messages.Count == 0 && SelectedConversation.Name == "New Chat")
            AutoNameConversation(msg);

        var userEntry = new ClaudeChatEntry { Role = "user", Content = msg };
        SelectedConversation.Messages.Add(userEntry);
        Application.Current.Dispatcher.Invoke(() => DisplayMessages.Add(EntryToDisplay(userEntry)));

        int replyIdx = AddPlaceholderBubble();
        var replyText = new StringBuilder();

        try
        {
            var (ok, _, err, tokens) = await _claude.StreamChatAsync(
                SelectedConversation.Messages,
                SelectedConversation.SystemPrompt,
                SelectedConversation.Model,
                chunk => { replyText.Append(chunk); UpdateBubble(replyIdx, replyText.ToString()); },
                ct);

            if (ok && replyText.Length > 0)
            {
                var entry = new ClaudeChatEntry { Role = "assistant", Content = replyText.ToString() };
                SelectedConversation.Messages.Add(entry);
                UpdateBubble(replyIdx, replyText.ToString());
                StatusText = $"{SelectedConversation.Model}  ·  {tokens} tokens out";
                SaveConversations();
            }
            else
            {
                RemoveBubble(replyIdx);
                RemoveLastUserMessage(msg);
                ChatError = err ?? "No response received";
                StatusText = "Error";
            }
        }
        catch (OperationCanceledException) { RemoveBubble(replyIdx); }
        catch (Exception ex) { ChatError = ex.Message; AppLogger.LogError("ClaudeTab: send failed", ex); }
        finally { IsSendingChat = false; }
    }

    // Orchestrated (with tools + agents) ─────────────────────────

    private async Task RunOrchestratedAsync(string msg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;
        if (SelectedConversation is null) { ChatError = "No conversation selected"; return; }
        if (!IsConfigured) { ChatError = "Claude API key not configured"; return; }

        ChatInput = string.Empty;
        IsSendingChat = true;
        ChatError = null;

        if (SelectedConversation.Messages.Count == 0 && SelectedConversation.Name == "New Chat")
            AutoNameConversation(msg);

        var userEntry = new ClaudeChatEntry { Role = "user", Content = msg };
        SelectedConversation.Messages.Add(userEntry);
        Application.Current.Dispatcher.Invoke(() => DisplayMessages.Add(EntryToDisplay(userEntry)));

        var systemPrompt = BuildOrchestratorSystemPrompt();
        int replyIdx = AddPlaceholderBubble();
        var replyText = new StringBuilder();

        AppLogger.Log("ClaudeAgent", $"Orchestrator starting — agents={Agents.Count} model={SelectedConversation.Model}");

        try
        {
            var (ok, finalText, err) = await _claude.RunAgentLoopAsync(
                SelectedConversation.Messages,
                systemPrompt,
                SelectedConversation.Model,
                OrchestratorTools,
                chunk => { replyText.Append(chunk); UpdateBubble(replyIdx, replyText.ToString()); },
                ExecuteAgentToolAsync,
                (agentName, task) =>
                {
                    AppLogger.Log("ClaudeAgent", $"Invoking [{agentName}]: {task[..Math.Min(task.Length, 120)]}");
                    Application.Current.Dispatcher.Invoke(() =>
                        DisplayMessages.Add(new ClawChatMessage
                        {
                            Role = "system",
                            Body = $"→ {agentName}: {task[..Math.Min(task.Length, 100)]}{(task.Length > 100 ? "…" : "")}"
                        }));
                    replyIdx = AddPlaceholderBubble(); // fresh bubble for orchestrator's next text
                    replyText.Clear();
                },
                ct);

            if (ok && (finalText?.Length ?? 0) > 0)
            {
                var entry = new ClaudeChatEntry { Role = "assistant", Content = finalText! };
                SelectedConversation.Messages.Add(entry);
                UpdateBubble(replyIdx, finalText!);
                RefreshStatusText();
                SaveConversations();
            }
            else if (!ok)
            {
                RemoveBubble(replyIdx);
                RemoveLastUserMessage(msg);
                ChatError = err ?? "No response received";
                AppLogger.Log("ClaudeAgent", $"Orchestrator error: {err}");
            }
        }
        catch (OperationCanceledException) { RemoveBubble(replyIdx); }
        catch (Exception ex) { ChatError = ex.Message; AppLogger.LogError("ClaudeAgent: failed", ex); }
        finally { IsSendingChat = false; }
    }

    private async Task<string> ExecuteAgentToolAsync(string agentName, string task, CancellationToken ct)
    {
        var agent = Agents.FirstOrDefault(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));
        if (agent is null)
        {
            var names = string.Join(", ", Agents.Select(a => a.Name));
            return $"Agent '{agentName}' not found. Available agents: {names}";
        }

        AppLogger.Log("ClaudeAgent", $"Running agent [{agent.Name}] model={agent.Model}");

        var agentHistory = new List<ClaudeChatEntry>
            { new() { Role = "user", Content = task } };

        var agentReply = new StringBuilder();
        var (ok, reply, err, _) = await _claude.StreamChatAsync(
            agentHistory,
            agent.SystemPrompt,
            agent.Model,
            chunk => agentReply.Append(chunk),
            ct);

        var result = ok ? (reply ?? agentReply.ToString()) : $"Error from {agent.Name}: {err}";
        AppLogger.Log("ClaudeAgent", $"Agent [{agent.Name}] returned {result.Length} chars");

        // Show abbreviated result in chat
        var preview = result.Length > 200 ? result[..200] + "…" : result;
        Application.Current.Dispatcher.Invoke(() =>
            DisplayMessages.Add(new ClawChatMessage
            {
                Role = "system",
                Body = $"← {agent.Name}: {preview}"
            }));

        return result;
    }

    private string BuildOrchestratorSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an AI orchestrator. When the user gives you a task:");
        sb.AppendLine("1. Analyze what needs to be done");
        sb.AppendLine("2. Decide whether to handle it directly or delegate parts to your specialized agents");
        sb.AppendLine("3. Use the invoke_agent tool to delegate tasks — pass all necessary context in the task");
        sb.AppendLine("4. Synthesize the agents' results and give the user a clear, complete final answer");
        sb.AppendLine();
        if (Agents.Count > 0)
        {
            sb.AppendLine("Your available agents:");
            foreach (var a in Agents)
                sb.AppendLine($"- **{a.Name}**: {a.Description}");
        }
        else
        {
            sb.AppendLine("No specialized agents are configured. Handle all tasks directly.");
        }
        if (SelectedConversation is not null && !string.IsNullOrWhiteSpace(SelectedConversation.SystemPrompt))
        {
            sb.AppendLine();
            sb.AppendLine("Additional instructions:");
            sb.AppendLine(SelectedConversation.SystemPrompt);
        }
        return sb.ToString();
    }

    // ── Bubble helpers ────────────────────────────────────────────

    private int AddPlaceholderBubble()
    {
        int idx = -1;
        Application.Current.Dispatcher.Invoke(() =>
        {
            DisplayMessages.Add(new ClawChatMessage { Role = "assistant", Body = "…" });
            idx = DisplayMessages.Count - 1;
        });
        return idx;
    }

    private void UpdateBubble(int idx, string body) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (idx >= 0 && idx < DisplayMessages.Count)
                DisplayMessages[idx] = new ClawChatMessage
                {
                    Role = "assistant",
                    Body = body,
                    Timestamp = DateTime.Now.ToString("HH:mm:ss")
                };
        });

    private void RemoveBubble(int idx) =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (idx >= 0 && idx < DisplayMessages.Count)
                DisplayMessages.RemoveAt(idx);
        });

    private void RemoveLastUserMessage(string msg)
    {
        var last = SelectedConversation?.Messages.LastOrDefault();
        if (last?.Role == "user" && last.Content == msg)
            SelectedConversation!.Messages.Remove(last);
    }

    private void AutoNameConversation(string firstMessage)
    {
        if (SelectedConversation is null) return;
        SelectedConversation.Name = firstMessage.Length > 40 ? firstMessage[..40] + "…" : firstMessage;
        OnPropertyChanged(nameof(SelectedConversation));
    }

    // ── Persistence ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private void LoadConversations()
    {
        try
        {
            if (!File.Exists(ConversationsPath)) return;
            var list = JsonSerializer.Deserialize<List<ClaudeConversation>>(
                File.ReadAllText(ConversationsPath));
            if (list is null) return;
            foreach (var c in list) Conversations.Add(c);
            if (Conversations.Count > 0) SelectedConversation = Conversations[0];
        }
        catch (Exception ex) { AppLogger.LogError("ClaudeVM: load conversations failed", ex); }
    }

    private void SaveConversations()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConversationsPath)!);
            File.WriteAllText(ConversationsPath,
                JsonSerializer.Serialize(Conversations.ToList(), _jsonOpts));
        }
        catch (Exception ex) { AppLogger.LogError("ClaudeVM: save conversations failed", ex); }
    }

    private void LoadAgents()
    {
        try
        {
            if (!File.Exists(AgentsPath)) return;
            var list = JsonSerializer.Deserialize<List<ClaudeAgent>>(
                File.ReadAllText(AgentsPath));
            if (list is null) return;
            foreach (var a in list) Agents.Add(a);
            if (Agents.Count > 0) SelectedAgent = Agents[0];
        }
        catch (Exception ex) { AppLogger.LogError("ClaudeVM: load agents failed", ex); }
    }

    private void SaveAgents()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AgentsPath)!);
            File.WriteAllText(AgentsPath,
                JsonSerializer.Serialize(Agents.ToList(), _jsonOpts));
        }
        catch (Exception ex) { AppLogger.LogError("ClaudeVM: save agents failed", ex); }
    }
}

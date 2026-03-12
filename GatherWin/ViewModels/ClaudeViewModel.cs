using System.Collections.ObjectModel;
using System.IO;
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

    // ── Model selector ────────────────────────────────────────────

    public static IReadOnlyList<ClaudeModelOption> Models { get; } = new[]
    {
        new ClaudeModelOption("claude-opus-4-6",           "Opus 4.6  (most capable)"),
        new ClaudeModelOption("claude-sonnet-4-6",         "Sonnet 4.6  (balanced)"),
        new ClaudeModelOption("claude-haiku-4-5-20251001", "Haiku 4.5  (fastest)"),
    };

    public ClaudeViewModel(string apiKey)
    {
        _claude = new ClaudeApiClient(apiKey);
        IsConfigured = _claude.IsConfigured;
        LoadConversations();
        if (!IsConfigured)
            StatusText = "Add a Claude API key in Options to use this tab";
    }

    partial void OnSelectedConversationChanged(ClaudeConversation? value)
    {
        DisplayMessages.Clear();
        ChatError = null;
        ShowSystemPrompt = false;

        if (value is null)
        {
            StatusText = "No conversation selected";
            return;
        }

        EditingSystemPrompt = value.SystemPrompt;
        RebuildDisplayMessages(value);
        StatusText = $"{value.Model}  ·  {value.Messages.Count / 2} exchange(s)";
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
        var conv = new ClaudeConversation
        {
            Model = Models[1].Id   // default: Sonnet
        };
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
        SelectedConversation = Conversations.Count > 0
            ? Conversations[Math.Max(0, idx - 1)]
            : null;
        SaveConversations();
    }

    [RelayCommand]
    private void ClearConversation()
    {
        if (SelectedConversation is null) return;
        SelectedConversation.Messages.Clear();
        DisplayMessages.Clear();
        StatusText = $"{SelectedConversation.Model}  ·  0 exchange(s)";
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

    // ── Model changes ─────────────────────────────────────────────

    // Called from XAML ComboBox via code-behind or by binding to SelectedConversation.Model
    public void SetModel(string modelId)
    {
        if (SelectedConversation is null) return;
        SelectedConversation.Model = modelId;
        StatusText = $"{modelId}  ·  {SelectedConversation.Messages.Count / 2} exchange(s)";
        SaveConversations();
    }

    // ── Send message ──────────────────────────────────────────────

    [RelayCommand]
    private async Task SendMessageAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;
        if (SelectedConversation is null) { ChatError = "No conversation selected"; return; }
        if (!IsConfigured) { ChatError = "Claude API key not configured — add it in Options"; return; }

        var msg = ChatInput.Trim();
        ChatInput = string.Empty;
        IsSendingChat = true;
        ChatError = null;

        // Auto-name the conversation from the first user message
        if (SelectedConversation.Messages.Count == 0 &&
            SelectedConversation.Name == "New Chat")
        {
            SelectedConversation.Name = msg.Length > 40 ? msg[..40] + "…" : msg;
            OnPropertyChanged(nameof(SelectedConversation));  // refresh list binding
        }

        // Add user message to stored history and display
        var userEntry = new ClaudeChatEntry { Role = "user", Content = msg };
        SelectedConversation.Messages.Add(userEntry);
        Application.Current.Dispatcher.Invoke(() =>
            DisplayMessages.Add(EntryToDisplay(userEntry)));

        // Placeholder for streaming reply
        int replyIdx = -1;
        Application.Current.Dispatcher.Invoke(() =>
        {
            DisplayMessages.Add(new ClawChatMessage { Role = "assistant", Body = "…" });
            replyIdx = DisplayMessages.Count - 1;
        });

        var replyText = new System.Text.StringBuilder();

        void UpdateBubble(string body) => Application.Current.Dispatcher.Invoke(() =>
        {
            if (replyIdx >= 0 && replyIdx < DisplayMessages.Count)
                DisplayMessages[replyIdx] = new ClawChatMessage
                {
                    Role = "assistant",
                    Body = body,
                    Timestamp = DateTime.Now.ToString("HH:mm:ss")
                };
        });

        try
        {
            var (ok, _, err, tokens) = await _claude.StreamChatAsync(
                SelectedConversation.Messages,
                SelectedConversation.SystemPrompt,
                SelectedConversation.Model,
                chunk => { replyText.Append(chunk); UpdateBubble(replyText.ToString()); },
                ct);

            if (ok && replyText.Length > 0)
            {
                var assistantEntry = new ClaudeChatEntry
                {
                    Role = "assistant",
                    Content = replyText.ToString()
                };
                SelectedConversation.Messages.Add(assistantEntry);
                UpdateBubble(replyText.ToString());
                StatusText = $"{SelectedConversation.Model}  ·  {tokens} tokens out";
                SaveConversations();
            }
            else
            {
                // Remove placeholder bubble on error
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (replyIdx >= 0 && replyIdx < DisplayMessages.Count)
                        DisplayMessages.RemoveAt(replyIdx);
                });
                // Remove the user message from history too so they can retry
                if (SelectedConversation.Messages.LastOrDefault() is { Role: "user" } last &&
                    last.Content == msg)
                    SelectedConversation.Messages.Remove(last);

                ChatError = err ?? "No response received";
                StatusText = "Error";
            }
        }
        catch (OperationCanceledException)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (replyIdx >= 0 && replyIdx < DisplayMessages.Count)
                    DisplayMessages.RemoveAt(replyIdx);
            });
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
            AppLogger.LogError("ClaudeTab: send failed", ex);
        }
        finally { IsSendingChat = false; }
    }

    // ── Persistence ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private void LoadConversations()
    {
        try
        {
            if (!File.Exists(ConversationsPath)) return;
            var json = File.ReadAllText(ConversationsPath);
            var list = JsonSerializer.Deserialize<List<ClaudeConversation>>(json);
            if (list is null) return;
            foreach (var c in list)
                Conversations.Add(c);
            if (Conversations.Count > 0)
                SelectedConversation = Conversations[0];
        }
        catch (Exception ex)
        {
            AppLogger.LogError("ClaudeVM: load conversations failed", ex);
        }
    }

    private void SaveConversations()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConversationsPath)!);
            var json = JsonSerializer.Serialize(Conversations.ToList(), _jsonOpts);
            File.WriteAllText(ConversationsPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("ClaudeVM: save conversations failed", ex);
        }
    }
}

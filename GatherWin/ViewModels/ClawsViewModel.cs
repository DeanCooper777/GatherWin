using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class ClawsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;
    private DateTimeOffset? _lastMessageTime;

    public ObservableCollection<ClawItem> Claws { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Not yet loaded";
    [ObservableProperty] private ClawItem? _selectedClaw;

    // ── Deploy form ───────────────────────────────────────────────

    [ObservableProperty] private bool _showDeployForm;
    [ObservableProperty] private string _deployName = string.Empty;
    [ObservableProperty] private string _deployInstructions = string.Empty;
    [ObservableProperty] private string _deployGithubRepo = string.Empty;
    [ObservableProperty] private string _deployClawType = "lite";
    [ObservableProperty] private string _deployAgentType = "clay";
    [ObservableProperty] private string _pbToken = string.Empty;
    [ObservableProperty] private bool _isDeploying;
    [ObservableProperty] private string? _deployError;
    [ObservableProperty] private string? _deploySuccess;

    // ── PocketBase auth note ──────────────────────────────────────

    [ObservableProperty] private bool _showPbTokenHelp;

    // ── Claw Chat ─────────────────────────────────────────────────

    public ObservableCollection<ClawChatMessage> ChatMessages { get; } = new();

    [ObservableProperty] private string _chatInput = string.Empty;
    [ObservableProperty] private bool _isChatLoading;
    [ObservableProperty] private bool _isSendingChat;
    [ObservableProperty] private string? _chatError;

    public ClawsViewModel(GatherApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedClawChanged(ClawItem? value)
    {
        ChatMessages.Clear();
        ChatError = null;
        _lastMessageTime = null;
        if (value is null) return;
        _ = LoadClawMessagesAsync(value, since: null, CancellationToken.None);
    }

    // ── Claw list ─────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadClawsAsync(CancellationToken ct)
    {
        if (IsLoading) return;
        if (string.IsNullOrWhiteSpace(PbToken))
        {
            StatusText = "Enter your Gather.is session token and click Refresh";
            return;
        }

        IsLoading = true;
        StatusText = "Loading claws...";

        try
        {
            var response = await _api.GetClawsAsync(ct, PbToken.Trim());
            Application.Current.Dispatcher.Invoke(() =>
            {
                Claws.Clear();
                if (response?.Claws is not null)
                    foreach (var claw in response.Claws)
                        Claws.Add(claw);
            });

            StatusText = Claws.Count > 0
                ? $"{Claws.Count} claw(s)"
                : "No claws deployed";

            AppLogger.Log("Claws", $"Loaded {Claws.Count} claws");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("Claws: load failed", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    // ── Deploy form ───────────────────────────────────────────────

    [RelayCommand]
    private void OpenDeployForm()
    {
        ShowDeployForm = true;
        DeployError = null;
        DeploySuccess = null;
        DeployName = string.Empty;
        DeployInstructions = string.Empty;
        DeployGithubRepo = string.Empty;
        DeployClawType = "lite";
        DeployAgentType = "clay";
    }

    [RelayCommand]
    private void CancelDeploy()
    {
        ShowDeployForm = false;
        DeployError = null;
    }

    [RelayCommand]
    private void TogglePbTokenHelp() => ShowPbTokenHelp = !ShowPbTokenHelp;

    [RelayCommand]
    private async Task DeployClawAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(DeployName))
        {
            DeployError = "Claw name is required";
            return;
        }
        if (string.IsNullOrWhiteSpace(PbToken))
        {
            DeployError = "Gather.is session token is required (see help below)";
            return;
        }

        IsDeploying = true;
        DeployError = null;
        DeploySuccess = null;

        try
        {
            var instructions = string.IsNullOrWhiteSpace(DeployInstructions) ? null : DeployInstructions.Trim();
            var repo = string.IsNullOrWhiteSpace(DeployGithubRepo) ? null : DeployGithubRepo.Trim();

            var (success, result, error) = await _api.DeployClawAsync(
                DeployName.Trim(), instructions, repo,
                DeployClawType, DeployAgentType, PbToken.Trim(), ct);

            if (success)
            {
                DeploySuccess = $"Claw '{DeployName}' deployed! Status: {result?.Status ?? "pending"}";
                ShowDeployForm = false;
                AppLogger.Log("Claws", $"Deployed claw: {DeployName}");
                await LoadClawsAsync(ct);
            }
            else
            {
                DeployError = error ?? "Deployment failed";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Claws: deploy failed", ex);
            DeployError = ex.Message;
        }
        finally { IsDeploying = false; }
    }

    // ── Chat ──────────────────────────────────────────────────────

    private async Task LoadClawMessagesAsync(ClawItem claw, DateTimeOffset? since, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PbToken)) return;
        if (claw.Id is null) return;

        IsChatLoading = true;
        try
        {
            var raw = await _api.GetClawMessagesRawAsync(claw.Id, since, PbToken.Trim(), ct);
            if (raw is null) return;
            AppendClawMessages(raw, clearFirst: since is null);
        }
        catch (Exception ex) { AppLogger.LogError("ClawChat: load failed", ex); }
        finally { IsChatLoading = false; }
    }

    private void AppendClawMessages(string json, bool clearFirst)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement arr = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("messages", out var m) ? m
                : default;

            if (arr.ValueKind != JsonValueKind.Array) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (clearFirst) ChatMessages.Clear();
                foreach (var item in arr.EnumerateArray())
                {
                    var id = TryGetString(item, "id") ?? string.Empty;
                    var authorId = TryGetString(item, "author_id") ?? string.Empty;
                    var body = TryGetString(item, "body") ?? string.Empty;
                    var created = TryGetString(item, "created") ?? string.Empty;

                    if (string.IsNullOrEmpty(body)) continue;
                    if (ChatMessages.Any(m => m.Id == id && id.Length > 0)) continue;

                    // author_id starts with "user:" for user messages, otherwise it's the claw
                    var role = authorId.StartsWith("user:") ? "user" : "assistant";

                    ChatMessages.Add(new ClawChatMessage { Id = id, Role = role, Body = body, Timestamp = created });

                    if (!string.IsNullOrEmpty(created) &&
                        DateTimeOffset.TryParse(created, out var ts) &&
                        (_lastMessageTime is null || ts > _lastMessageTime))
                        _lastMessageTime = ts;
                }
            });
        }
        catch (Exception ex) { AppLogger.LogError("ClawChat: parse failed", ex); }
    }

    private static string? TryGetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    [RelayCommand]
    private async Task SendClawMessageAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;
        if (SelectedClaw?.Id is null) { ChatError = "No claw selected"; return; }
        if (string.IsNullOrWhiteSpace(PbToken)) { ChatError = "Session token required"; return; }

        var msg = ChatInput.Trim();
        ChatInput = string.Empty;
        IsSendingChat = true;
        ChatError = null;

        Application.Current.Dispatcher.Invoke(() =>
            ChatMessages.Add(new ClawChatMessage { Role = "user", Body = msg }));

        try
        {
            // Add a placeholder for the streaming reply; capture its index
            int replyIdx = -1;
            Application.Current.Dispatcher.Invoke(() =>
            {
                ChatMessages.Add(new ClawChatMessage { Role = "assistant", Body = "…" });
                replyIdx = ChatMessages.Count - 1;
            });

            var replyText = new System.Text.StringBuilder();
            var (ok, _, err) = await _api.PostClawMessageStreamAsync(
                SelectedClaw.Id, msg, PbToken.Trim(),
                chunk =>
                {
                    replyText.Append(chunk);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (replyIdx >= 0 && replyIdx < ChatMessages.Count)
                            ChatMessages[replyIdx] = new ClawChatMessage
                            {
                                Role = "assistant",
                                Body = replyText.ToString(),
                                Timestamp = DateTime.Now.ToString("HH:mm:ss")
                            };
                    });
                }, ct);

            if (!ok)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (replyIdx >= 0 && replyIdx < ChatMessages.Count)
                        ChatMessages.RemoveAt(replyIdx);
                });
                ChatError = err;
            }
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
            AppLogger.LogError("ClawChat: send failed", ex);
        }
        finally { IsSendingChat = false; }
    }

    // ── Helpers ───────────────────────────────────────────────────

    public static string FormatStatus(string? status) => status switch
    {
        "running" => "Running",
        "stopped" => "Stopped",
        "pending" => "Pending",
        "error" => "Error",
        _ => status ?? "Unknown"
    };

    public static string StatusColor(string? status) => status switch
    {
        "running" => "#27AE60",
        "stopped" => "#95A5A6",
        "error" => "#E74C3C",
        _ => "#F39C12"
    };
}

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

    // ── Panel mode ────────────────────────────────────────────────

    [ObservableProperty] private string _clawPanel = "chat"; // chat | settings | env | logs

    public bool IsClawPanelChat     => ClawPanel == "chat";
    public bool IsClawPanelSettings => ClawPanel == "settings";
    public bool IsClawPanelEnv      => ClawPanel == "env";
    public bool IsClawPanelLogs     => ClawPanel == "logs";

    partial void OnClawPanelChanged(string value)
    {
        OnPropertyChanged(nameof(IsClawPanelChat));
        OnPropertyChanged(nameof(IsClawPanelSettings));
        OnPropertyChanged(nameof(IsClawPanelEnv));
        OnPropertyChanged(nameof(IsClawPanelLogs));

        if (SelectedClaw is null) return;
        if (value == "settings") LoadSettingsFromClaw(SelectedClaw);
        else if (value == "env")  _ = LoadEnvAsync(SelectedClaw, CancellationToken.None);
        else if (value == "logs") _ = RefreshLogsAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ShowClawPanel(string panel) => ClawPanel = panel;

    // ── Settings ──────────────────────────────────────────────────

    [ObservableProperty] private bool _settingsIsPublic;
    [ObservableProperty] private int _settingsHeartbeatInterval;
    [ObservableProperty] private string _settingsHeartbeatInstruction = string.Empty;
    [ObservableProperty] private bool _isSavingSettings;
    [ObservableProperty] private string? _settingsError;
    [ObservableProperty] private string? _settingsSuccess;

    private void LoadSettingsFromClaw(ClawItem claw)
    {
        SettingsIsPublic = claw.IsPublic;
        SettingsHeartbeatInterval = claw.HeartbeatInterval;
        SettingsHeartbeatInstruction = claw.HeartbeatInstruction ?? string.Empty;
        SettingsError = null;
        SettingsSuccess = null;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        if (SelectedClaw?.Id is null || string.IsNullOrWhiteSpace(PbToken)) return;
        IsSavingSettings = true;
        SettingsError = null;
        SettingsSuccess = null;
        try
        {
            var ok = await _api.PatchClawAsync(
                SelectedClaw.Id,
                SettingsIsPublic,
                SettingsHeartbeatInterval,
                SettingsHeartbeatInstruction,
                PbToken.Trim(), ct);

            if (ok)
            {
                SettingsSuccess = "Settings saved";
                // Update the local ClawItem so re-entering settings shows fresh values
                SelectedClaw.IsPublic = SettingsIsPublic;
                SelectedClaw.HeartbeatInterval = SettingsHeartbeatInterval;
                SelectedClaw.HeartbeatInstruction = SettingsHeartbeatInstruction;
                AppLogger.Log("Claws", $"Settings saved for {SelectedClaw.Name}");
            }
            else
            {
                SettingsError = "Failed to save settings";
            }
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            AppLogger.LogError("Claws: save settings failed", ex);
        }
        finally { IsSavingSettings = false; }
    }

    // ── Environment Variables ─────────────────────────────────────

    [ObservableProperty] private string _envModelProvider = string.Empty;
    [ObservableProperty] private string _envAnthropicApiKey = string.Empty;
    [ObservableProperty] private string _envAnthropicApiBase = string.Empty;
    [ObservableProperty] private string _envAnthropicModel = string.Empty;
    [ObservableProperty] private string _envTelegramBot = string.Empty;
    [ObservableProperty] private string _envTelegramChatId = string.Empty;
    [ObservableProperty] private bool _envRestartAfterSave = true;
    [ObservableProperty] private bool _isLoadingEnv;
    [ObservableProperty] private bool _isSavingEnv;
    [ObservableProperty] private string? _envError;
    [ObservableProperty] private string? _envSuccess;

    private async Task LoadEnvAsync(ClawItem claw, CancellationToken ct)
    {
        if (claw.Id is null || string.IsNullOrWhiteSpace(PbToken)) return;
        IsLoadingEnv = true;
        EnvError = null;
        EnvSuccess = null;
        try
        {
            var vars = await _api.GetClawEnvAsync(claw.Id, PbToken.Trim(), ct);
            if (vars != null)
            {
                EnvModelProvider    = vars.GetValueOrDefault("MODEL_PROVIDER", string.Empty);
                EnvAnthropicApiKey  = vars.GetValueOrDefault("ANTHROPIC_API_KEY", string.Empty);
                EnvAnthropicApiBase = vars.GetValueOrDefault("ANTHROPIC_API_BASE", string.Empty);
                EnvAnthropicModel   = vars.GetValueOrDefault("ANTHROPIC_MODEL", string.Empty);
                EnvTelegramBot      = vars.GetValueOrDefault("TELEGRAM_BOT", string.Empty);
                EnvTelegramChatId   = vars.GetValueOrDefault("TELEGRAM_CHAT_ID", string.Empty);
            }
        }
        catch (Exception ex) { EnvError = ex.Message; }
        finally { IsLoadingEnv = false; }
    }

    [RelayCommand]
    private async Task SaveEnvAsync(CancellationToken ct)
    {
        if (SelectedClaw?.Id is null || string.IsNullOrWhiteSpace(PbToken)) return;
        IsSavingEnv = true;
        EnvError = null;
        EnvSuccess = null;
        try
        {
            var vars = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(EnvModelProvider))    vars["MODEL_PROVIDER"]     = EnvModelProvider;
            if (!string.IsNullOrEmpty(EnvAnthropicApiKey))  vars["ANTHROPIC_API_KEY"]  = EnvAnthropicApiKey;
            if (!string.IsNullOrEmpty(EnvAnthropicApiBase)) vars["ANTHROPIC_API_BASE"] = EnvAnthropicApiBase;
            if (!string.IsNullOrEmpty(EnvAnthropicModel))   vars["ANTHROPIC_MODEL"]    = EnvAnthropicModel;
            if (!string.IsNullOrEmpty(EnvTelegramBot))      vars["TELEGRAM_BOT"]       = EnvTelegramBot;
            if (!string.IsNullOrEmpty(EnvTelegramChatId))   vars["TELEGRAM_CHAT_ID"]   = EnvTelegramChatId;

            var ok = await _api.PutClawEnvAsync(SelectedClaw.Id, vars, EnvRestartAfterSave, PbToken.Trim(), ct);
            if (ok)
                EnvSuccess = EnvRestartAfterSave ? "Env saved — claw restarted" : "Env saved";
            else
                EnvError = "Failed to save env vars";
        }
        catch (Exception ex) { EnvError = ex.Message; }
        finally { IsSavingEnv = false; }
    }

    // ── Logs ──────────────────────────────────────────────────────

    [ObservableProperty] private string _logsText = string.Empty;
    [ObservableProperty] private bool _isLoadingLogs;

    [RelayCommand]
    private async Task RefreshLogsAsync(CancellationToken ct)
    {
        if (SelectedClaw?.Id is null || string.IsNullOrWhiteSpace(PbToken)) return;
        IsLoadingLogs = true;
        try
        {
            var logs = await _api.GetClawLogsAsync(SelectedClaw.Id, 200, PbToken.Trim(), ct);
            LogsText = logs ?? "(no logs available)";
        }
        catch (Exception ex) { LogsText = $"Error: {ex.Message}"; }
        finally { IsLoadingLogs = false; }
    }

    // ── Restart ───────────────────────────────────────────────────

    [ObservableProperty] private bool _isRestarting;

    [RelayCommand]
    private async Task RestartClawAsync(CancellationToken ct)
    {
        if (SelectedClaw?.Id is null || string.IsNullOrWhiteSpace(PbToken)) return;
        IsRestarting = true;
        try
        {
            var ok = await _api.RestartClawAsync(SelectedClaw.Id, PbToken.Trim(), ct);
            StatusText = ok ? $"Restarted {SelectedClaw.Name}" : "Restart failed";
            if (ok) AppLogger.Log("Claws", $"Restarted claw: {SelectedClaw.Name}");
        }
        catch (Exception ex) { StatusText = $"Restart error: {ex.Message}"; }
        finally { IsRestarting = false; }
    }

    // ── Delete ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task DeleteClawAsync(CancellationToken ct)
    {
        if (SelectedClaw?.Id is null || string.IsNullOrWhiteSpace(PbToken)) return;
        var clawToDelete = SelectedClaw;
        var ok = await _api.DeleteClawAsync(clawToDelete.Id, PbToken.Trim(), ct);
        if (ok)
        {
            var name = clawToDelete.Name ?? clawToDelete.Id;
            Application.Current.Dispatcher.Invoke(() =>
            {
                Claws.Remove(clawToDelete);
                SelectedClaw = null;
            });
            StatusText = $"Deleted {name}";
            AppLogger.Log("Claws", $"Deleted claw: {name}");
        }
        else
        {
            StatusText = "Delete failed";
        }
    }

    public ClawsViewModel(GatherApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedClawChanged(ClawItem? value)
    {
        ChatMessages.Clear();
        ChatError = null;
        _lastMessageTime = null;
        ClawPanel = "chat";
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

            // Parse all items, sort oldest-first, then append
            var parsed = new List<(string Id, string Role, string Body, string Created, DateTimeOffset Ts)>();
            foreach (var item in arr.EnumerateArray())
            {
                var id = TryGetString(item, "id") ?? string.Empty;
                var authorId = TryGetString(item, "author_id") ?? string.Empty;
                var body = TryGetString(item, "body") ?? string.Empty;
                var created = TryGetString(item, "created") ?? string.Empty;
                if (string.IsNullOrEmpty(body)) continue;
                DateTimeOffset.TryParse(created, out var ts);
                var role = authorId.StartsWith("user:") ? "user" : "assistant";
                parsed.Add((id, role, body, created, ts));
            }
            parsed.Sort((a, b) => a.Ts.CompareTo(b.Ts));

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (clearFirst) ChatMessages.Clear();
                foreach (var (id, role, body, created, ts) in parsed)
                {
                    if (id.Length > 0 && ChatMessages.Any(m => m.Id == id)) continue;
                    ChatMessages.Add(new ClawChatMessage { Id = id, Role = role, Body = body, Timestamp = created });
                    if (_lastMessageTime is null || ts > _lastMessageTime)
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
                }, ct,
                finalText =>
                {
                    // "end" event — replace placeholder with the final complete reply
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (replyIdx >= 0 && replyIdx < ChatMessages.Count)
                            ChatMessages[replyIdx] = new ClawChatMessage
                            {
                                Role = "assistant",
                                Body = finalText,
                                Timestamp = DateTime.Now.ToString("HH:mm:ss")
                            };
                    });
                });

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

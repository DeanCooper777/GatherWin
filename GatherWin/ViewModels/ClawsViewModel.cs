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
    private readonly Dictionary<string, string> _conversationIds = new(); // clawId → conversationId

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
        if (value is null) return;
        AppLogger.Log("ClawChat", $"Selected claw: {value.Name}, url={value.Url}");
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

    [RelayCommand]
    private async Task SendClawMessageAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;
        if (SelectedClaw?.Url is null)
        {
            ChatError = "No claw selected";
            return;
        }
        if (string.IsNullOrWhiteSpace(PbToken))
        {
            ChatError = "Session token required";
            return;
        }

        var msg = ChatInput.Trim();
        ChatInput = string.Empty;
        IsSendingChat = true;
        ChatError = null;

        // Add user message to display immediately
        Application.Current.Dispatcher.Invoke(() =>
            ChatMessages.Add(new ClawChatMessage { Role = "user", Body = msg }));

        try
        {
            _conversationIds.TryGetValue(SelectedClaw.Id!, out var convId);

            var (ok, responseBody, err) = await _api.SendClawChatAsync(
                SelectedClaw.Url, msg, convId, PbToken.Trim(), ct);

            if (!ok)
            {
                ChatError = err;
                return;
            }

            // Parse the response to extract the assistant reply and conversation_id
            var (reply, newConvId) = ParseClawChatResponse(responseBody!);

            if (newConvId is not null)
                _conversationIds[SelectedClaw.Id!] = newConvId;

            if (reply is not null)
                Application.Current.Dispatcher.Invoke(() =>
                    ChatMessages.Add(new ClawChatMessage { Role = "assistant", Body = reply }));
            else
                AppLogger.Log("ClawChat", $"Could not parse reply from: {responseBody}");
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
            AppLogger.LogError("ClawChat: send failed", ex);
        }
        finally { IsSendingChat = false; }
    }

    private static (string? Reply, string? ConversationId) ParseClawChatResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? reply = null;
            string? convId = null;

            // Try common response shapes
            if (root.TryGetProperty("message", out var msgProp))
                reply = msgProp.GetString();
            else if (root.TryGetProperty("content", out var contentProp))
                reply = contentProp.GetString();
            else if (root.TryGetProperty("response", out var respProp))
                reply = respProp.GetString();
            else if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                // OpenAI-style response
                var first = choices.EnumerateArray().FirstOrDefault();
                if (first.TryGetProperty("message", out var choiceMsg) &&
                    choiceMsg.TryGetProperty("content", out var choiceContent))
                    reply = choiceContent.GetString();
            }
            else if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                // Last assistant message in messages array
                foreach (var m in msgs.EnumerateArray())
                {
                    if (m.TryGetProperty("role", out var roleProp) && roleProp.GetString() == "assistant" &&
                        m.TryGetProperty("content", out var c))
                        reply = c.GetString();
                }
            }

            if (root.TryGetProperty("conversation_id", out var cidProp))
                convId = cidProp.GetString();

            return (reply, convId);
        }
        catch { return (null, null); }
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

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class ClawsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;
    private readonly Dictionary<string, string> _clawChannelMap = new(); // clawId → channelId

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

    public ObservableCollection<ChannelMessage> ChatMessages { get; } = new();

    [ObservableProperty] private string _chatChannelId = string.Empty;
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
        if (value is null)
        {
            ChatMessages.Clear();
            ChatChannelId = string.Empty;
            ChatError = null;
            return;
        }
        _ = LoadClawChatAsync(value, CancellationToken.None);
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

    private async Task LoadClawChatAsync(ClawItem claw, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(claw.AgentId))
        {
            ChatError = "This claw has no agent ID — cannot open chat";
            return;
        }

        IsChatLoading = true;
        ChatError = null;
        Application.Current.Dispatcher.Invoke(ChatMessages.Clear);
        ChatChannelId = string.Empty;

        try
        {
            if (!_clawChannelMap.TryGetValue(claw.Id!, out var channelId))
            {
                channelId = await FindOrCreateClawChannelAsync(claw, ct);
                if (channelId is null) return;
                _clawChannelMap[claw.Id!] = channelId;
            }

            ChatChannelId = channelId;
            await RefreshChatMessagesAsync(channelId, ct);
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
            AppLogger.LogError("ClawChat: load failed", ex);
        }
        finally { IsChatLoading = false; }
    }

    private async Task<string?> FindOrCreateClawChannelAsync(ClawItem claw, CancellationToken ct)
    {
        var channelName = $"Claw: {claw.Name}";

        // Look for an existing channel with this name
        var list = await _api.GetChannelsAsync(ct);
        var existing = list?.Channels?.FirstOrDefault(c => c.Name == channelName);
        if (existing?.Id is not null)
        {
            AppLogger.Log("ClawChat", $"Found existing channel '{channelName}' ({existing.Id})");
            return existing.Id;
        }

        // Create a new private channel
        var (ok, channelId, err) = await _api.CreateChannelAsync(
            channelName, $"Chat with claw {claw.Name}", ct);

        if (!ok || channelId is null)
        {
            ChatError = err ?? "Failed to create chat channel";
            return null;
        }

        AppLogger.Log("ClawChat", $"Created channel '{channelName}' ({channelId})");

        // Invite the claw's agent
        var (inviteOk, inviteErr) = await _api.InviteToChannelAsync(channelId, claw.AgentId!, ct);
        if (!inviteOk)
            AppLogger.LogError($"ClawChat: invite failed — {inviteErr}", null);

        return channelId;
    }

    private async Task RefreshChatMessagesAsync(string channelId, CancellationToken ct)
    {
        var msgs = await _api.GetChannelMessagesAsync(channelId, null, ct);
        Application.Current.Dispatcher.Invoke(() =>
        {
            ChatMessages.Clear();
            if (msgs?.Messages is not null)
                foreach (var m in msgs.Messages)
                    ChatMessages.Add(m);
        });
        AppLogger.Log("ClawChat", $"Loaded {msgs?.Messages?.Count ?? 0} messages from {channelId}");
    }

    [RelayCommand]
    private async Task SendClawMessageAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ChatInput) || string.IsNullOrWhiteSpace(ChatChannelId))
            return;

        var msg = ChatInput.Trim();
        ChatInput = string.Empty;
        IsSendingChat = true;
        ChatError = null;

        try
        {
            var (ok, err) = await _api.PostChannelMessageAsync(ChatChannelId, msg, ct);
            if (!ok)
                ChatError = err ?? "Failed to send message";
            else
                await RefreshChatMessagesAsync(ChatChannelId, ct);
        }
        catch (Exception ex)
        {
            ChatError = ex.Message;
            AppLogger.LogError("ClawChat: send failed", ex);
        }
        finally { IsSendingChat = false; }
    }

    /// <summary>Called by MainViewModel when a channel message arrives for the active chat channel.</summary>
    public void OnNewChannelMessage(string channelId, ChannelMessage message)
    {
        if (channelId != ChatChannelId) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Dedup by ID
            if (message.Id is not null && ChatMessages.Any(m => m.Id == message.Id)) return;
            ChatMessages.Add(message);
        });
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

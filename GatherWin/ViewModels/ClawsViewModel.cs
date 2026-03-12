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

    public ClawsViewModel(GatherApiClient api)
    {
        _api = api;
    }

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

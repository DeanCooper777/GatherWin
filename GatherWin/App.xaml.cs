using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GatherWin.Services;
using GatherWin.ViewModels;
using GatherWin.Views;

namespace GatherWin;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLogger.Log("App", "Starting up...");

        // Load configuration (appsettings.Local.json overrides base and is gitignored)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build();

        var gatherSection = configuration.GetSection("Gather");
        var agentId = gatherSection["AgentId"] ?? string.Empty;
        var watchedPostIds = (gatherSection["WatchedPostIds"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        var pollInterval = int.TryParse(gatherSection["PollIntervalSeconds"], out var pi) ? pi : 60;
        var keysDirectory = gatherSection["KeysDirectory"] ?? string.Empty;
        var claudeApiKeyRaw = gatherSection["ClaudeApiKey"] ?? string.Empty;
        var claudeApiKey = string.Empty;
        if (!string.IsNullOrEmpty(claudeApiKeyRaw))
        {
            try
            {
                if (CredentialProtector.IsPlaintext(claudeApiKeyRaw))
                {
                    // Migration: plaintext key found, will encrypt on next save
                    claudeApiKey = claudeApiKeyRaw;
                    AppLogger.Log("App", "Plaintext API key detected — will encrypt on next save");
                }
                else
                {
                    claudeApiKey = CredentialProtector.Unprotect(claudeApiKeyRaw);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("App: Failed to decrypt Claude API key", ex);
                claudeApiKey = string.Empty;
            }
        }
        var newBadgeDurationMinutes = int.TryParse(gatherSection["NewBadgeDurationMinutes"], out var nbdm) ? nbdm : 30;

        if (string.IsNullOrEmpty(agentId))
        {
            // Prevent WPF from shutting down when the setup dialog closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var setup = new SetupWindow();
            if (setup.ShowDialog() == true)
            {
                agentId = setup.AgentId;
                claudeApiKey = setup.ClaudeApiKey;
                SaveLocalSettings(agentId, string.Join(",", watchedPostIds), pollInterval,
                    keysDirectory, claudeApiKey, newBadgeDurationMinutes);
            }
            else
            {
                Shutdown();
                return;
            }

            // Restore normal shutdown mode for the main window
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        // Shared JSON options (snake_case for Gather API)
        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Shared HttpClient
        var httpClient = new HttpClient
        {
            DefaultRequestHeaders = { { "Accept", "application/json" } },
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Build services
        var services = new ServiceCollection();

        var authService = new GatherAuthService(httpClient, jsonOpts, keysDirectory);
        var apiClient = new GatherApiClient(httpClient, jsonOpts, authService);
        var claudeClient = new ClaudeApiClient(claudeApiKey);

        services.AddSingleton(authService);
        services.AddSingleton(apiClient);
        services.AddSingleton(claudeClient);
        services.AddSingleton(sp => new MainViewModel(
            apiClient, authService, agentId, watchedPostIds, pollInterval, keysDirectory,
            claudeApiKey, newBadgeDurationMinutes));
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var vm = _serviceProvider?.GetService<MainViewModel>();
        vm?.Shutdown();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Writes settings to appsettings.Local.json in the application directory.
    /// </summary>
    public static void SaveLocalSettings(string agentId, string watchedPostIds, int pollInterval,
        string keysDirectory, string claudeApiKey, int newBadgeDurationMinutes)
    {
        // Encrypt the Claude API key before persisting
        var storedApiKey = string.Empty;
        if (!string.IsNullOrEmpty(claudeApiKey))
        {
            try
            {
                storedApiKey = CredentialProtector.Protect(claudeApiKey);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("App: Failed to encrypt Claude API key, storing empty", ex);
            }
        }

        var settings = new Dictionary<string, object>
        {
            ["Gather"] = new Dictionary<string, object>
            {
                ["AgentId"] = agentId,
                ["WatchedPostIds"] = watchedPostIds,
                ["PollIntervalSeconds"] = pollInterval,
                ["KeysDirectory"] = keysDirectory,
                ["ClaudeApiKey"] = storedApiKey,
                ["NewBadgeDurationMinutes"] = newBadgeDurationMinutes
            }
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.Local.json");
        File.WriteAllText(path, json);
        AppLogger.Log("App", $"Settings saved to {path}");
    }
}

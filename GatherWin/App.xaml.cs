using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GatherWin.Services;
using GatherWin.ViewModels;

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

        if (string.IsNullOrEmpty(agentId))
        {
            MessageBox.Show(
                "No Agent ID configured.\n\n" +
                "Edit appsettings.json and set your Gather agent ID in the \"AgentId\" field, " +
                "then restart the application.\n\n" +
                "See README.md for setup instructions.",
                "GatherWin — Setup Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

        services.AddSingleton(authService);
        services.AddSingleton(apiClient);
        services.AddSingleton(sp => new MainViewModel(
            apiClient, authService, agentId, watchedPostIds, pollInterval, keysDirectory));
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
}

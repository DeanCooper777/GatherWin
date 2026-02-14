using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using GatherWin.Services;
using GatherWin.ViewModels;

namespace GatherWin.Tests.ViewModels;

/// <summary>
/// Tests for MainViewModel lifecycle and coordination.
/// Note: Many methods require a WPF Dispatcher context (Application.Current),
/// so we focus on constructor behavior and property logic here.
/// </summary>
public class MainViewModelTests
{
    private static (MainViewModel vm, GatherApiClient api, GatherAuthService auth) CreateViewModel()
    {
        var http = new HttpClient();
        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var auth = new GatherAuthService(http, jsonOpts, "/nonexistent");
        var api = new GatherApiClient(http, jsonOpts, auth);
        var vm = new MainViewModel(api, auth, "test-agent", ["post1", "post2"], 60, "/keys", "test-key", 30);
        return (vm, api, auth);
    }

    [Fact]
    public void Constructor_InitializesSubViewModels()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.NotNull(vm.Account);
        Assert.NotNull(vm.Comments);
        Assert.NotNull(vm.Inbox);
        Assert.NotNull(vm.Feed);
        Assert.NotNull(vm.Channels);
        Assert.NotNull(vm.WhatsNew);
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.Equal("test-agent", vm.AgentId);
        Assert.Equal("test-key", vm.ClaudeApiKey);
        Assert.Equal(30, vm.NewBadgeDurationMinutes);
        Assert.False(vm.IsConnected);
        Assert.False(vm.IsConnecting);
        Assert.Equal("Disconnected", vm.StatusMessage);
    }

    [Fact]
    public void Constructor_WiresSubscribeCallback()
    {
        var (vm, _, _) = CreateViewModel();

        // The Feed.SubscribeRequested callback should be wired
        Assert.NotNull(vm.Feed.SubscribeRequested);
    }

    [Fact]
    public void Constructor_WiresUnsubscribeCallback()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.NotNull(vm.Comments.UnsubscribeRequested);
    }

    [Fact]
    public void AllActivity_InitiallyEmpty()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.Empty(vm.AllActivity);
    }

    [Fact]
    public void FontScale_DefaultsFromOptions()
    {
        var (vm, _, _) = CreateViewModel();

        // Default WhatsNewOptions.FontScalePercent is 100, so FontScale should be 1.0
        Assert.Equal(1.0, vm.FontScale);
    }
}

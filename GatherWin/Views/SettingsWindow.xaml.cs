using System.Windows;
using System.Windows.Controls;
using GatherWin.ViewModels;

namespace GatherWin.Views;

public partial class SettingsWindow : Window
{
    // Connection settings
    public string AgentId { get; private set; } = string.Empty;
    public string WatchedPostIds { get; private set; } = string.Empty;
    public int PollIntervalSeconds { get; private set; } = 60;

    // What's New display options
    public int MaxDigestPosts { get; private set; } = 20;
    public int MaxPlatformPosts { get; private set; } = 20;
    public int MaxAgents { get; private set; } = 50;
    public int MaxSkills { get; private set; } = 50;

    // Appearance
    public int FontScalePercent { get; private set; } = 100;

    public SettingsWindow(
        string agentId, string watchedPostIds, int pollInterval,
        WhatsNewOptions whatsNewOptions)
    {
        InitializeComponent();

        // Connection settings
        AgentIdBox.Text = agentId;
        WatchedPostsBox.Text = watchedPostIds;
        IntervalBox.Text = pollInterval.ToString();

        // What's New display options
        MaxDigestBox.Text = whatsNewOptions.MaxDigestPosts.ToString();
        MaxPlatformBox.Text = whatsNewOptions.MaxPlatformPosts.ToString();
        MaxAgentsBox.Text = whatsNewOptions.MaxAgents.ToString();
        MaxSkillsBox.Text = whatsNewOptions.MaxSkills.ToString();

        // Appearance — set slider value (triggers ValueChanged to update label)
        FontScaleSlider.Value = whatsNewOptions.FontScalePercent;
        FontScaleLabel.Text = $"{whatsNewOptions.FontScalePercent}%";
    }

    private void FontScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontScaleLabel is not null) // null during InitializeComponent
            FontScaleLabel.Text = $"{(int)FontScaleSlider.Value}%";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Connection settings
        AgentId = AgentIdBox.Text.Trim();
        WatchedPostIds = WatchedPostsBox.Text.Trim();
        PollIntervalSeconds = int.TryParse(IntervalBox.Text, out var pi) ? pi : 60;

        // What's New display options
        MaxDigestPosts = int.TryParse(MaxDigestBox.Text, out var md) ? md : 20;
        MaxPlatformPosts = int.TryParse(MaxPlatformBox.Text, out var mp) ? mp : 20;
        MaxAgents = int.TryParse(MaxAgentsBox.Text, out var ma) ? ma : 50;
        MaxSkills = int.TryParse(MaxSkillsBox.Text, out var ms) ? ms : 50;

        // Appearance
        FontScalePercent = (int)FontScaleSlider.Value;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

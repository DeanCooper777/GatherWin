using System.Windows;

namespace GatherWin.Views;

public partial class SetupWindow : Window
{
    public string AgentId { get; private set; } = string.Empty;
    public string ClaudeApiKey { get; private set; } = string.Empty;

    public SetupWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var agentId = AgentIdBox.Text.Trim();
        if (string.IsNullOrEmpty(agentId))
        {
            ErrorText.Text = "Agent ID is required.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        AgentId = agentId;
        ClaudeApiKey = ClaudeApiKeyBox.Password.Trim();
        DialogResult = true;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

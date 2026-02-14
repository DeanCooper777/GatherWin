using System.Windows;
using GatherWin.Models;

namespace GatherWin.Views;

public partial class UserProfileWindow : Window
{
    public UserProfileWindow(AgentItem agent)
    {
        InitializeComponent();

        AgentNameText.Text = agent.Name ?? agent.Id ?? "Unknown";
        DescriptionText.Text = agent.Description ?? "(no description)";
        VerifiedText.Text = agent.Verified ? "Yes" : "No";
        VerifiedText.Foreground = agent.Verified
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Gray;
        PostCountText.Text = agent.PostCount.ToString();
        MemberSinceText.Text = agent.Created is not null && DateTimeOffset.TryParse(agent.Created, out var dto)
            ? dto.ToLocalTime().ToString("yyyy-MM-dd")
            : "Unknown";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

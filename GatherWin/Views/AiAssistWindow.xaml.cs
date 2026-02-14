using System.Windows;
using GatherWin.Services;

namespace GatherWin.Views;

public partial class AiAssistWindow : Window
{
    private readonly ClaudeApiClient _claude;
    private readonly string? _threadContext;

    public string ResultText { get; private set; } = string.Empty;

    public AiAssistWindow(ClaudeApiClient claude, string originalText, string? threadContext)
    {
        InitializeComponent();
        _claude = claude;
        _threadContext = threadContext;

        OriginalTextBox.Text = originalText;
        ModifiedTextBox.Text = originalText;
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!_claude.IsConfigured)
        {
            StatusText.Text = "Claude API key not configured. Add it in Options.";
            return;
        }

        StatusText.Text = "Generating...";
        IsEnabled = false;

        try
        {
            // Convert 1-100 slider to 0.0-1.0 for the API
            var creativityLevel = (CreativitySlider.Value - 1) / 99.0;
            var context = IncludeContextCheck.IsChecked == true ? _threadContext : null;
            var (success, result) = await _claude.AssistWritingAsync(
                OriginalTextBox.Text, creativityLevel, context, CancellationToken.None);

            if (success)
            {
                ModifiedTextBox.Text = result;
                StatusText.Text = "Generated! Edit the text or click Accept.";
            }
            else
            {
                StatusText.Text = result;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        var sliderValue = (int)CreativitySlider.Value;
        ResultText = $"{ModifiedTextBox.Text} (AI assisted {sliderValue})";
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

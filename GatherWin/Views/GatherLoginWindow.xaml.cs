using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace GatherWin.Views;

public partial class GatherLoginWindow : Window
{
    private readonly Action<string> _onTokenObtained;
    private bool _tokenFound;

    public GatherLoginWindow(Action<string> onTokenObtained)
    {
        InitializeComponent();
        _onTokenObtained = onTokenObtained;
        WebView.NavigationCompleted += WebView_NavigationCompleted;
    }

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_tokenFound) return;
        try
        {
            // Read pocketbase_auth from localStorage and extract the token field
            var result = await WebView.ExecuteScriptAsync(
                "(function() { " +
                "  try { " +
                "    var s = localStorage.getItem('pocketbase_auth'); " +
                "    if (!s) return null; " +
                "    var auth = JSON.parse(s); " +
                "    return auth && auth.token ? auth.token : null; " +
                "  } catch(e) { return null; } " +
                "})()");

            // ExecuteScriptAsync returns a JSON-encoded string (with surrounding quotes) or "null"
            if (result == "null" || string.IsNullOrEmpty(result)) return;

            // Strip surrounding JSON quotes
            var token = System.Text.Json.JsonSerializer.Deserialize<string>(result);
            if (string.IsNullOrEmpty(token)) return;

            _tokenFound = true;
            StatusText.Text = $"Token detected! ({token[..Math.Min(token.Length, 20)]}...)";
            StatusText.Foreground = System.Windows.Media.Brushes.Green;

            _onTokenObtained(token);
        }
        catch { /* page may not support localStorage (e.g. error pages) */ }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}

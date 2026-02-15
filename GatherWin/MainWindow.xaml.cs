using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GatherWin.Converters;
using GatherWin.Models;
using GatherWin.Services;
using GatherWin.ViewModels;
using GatherWin.Views;

namespace GatherWin;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly System.ComponentModel.PropertyChangedEventHandler _onPropertyChanged;
    private readonly EventHandler _onNewActivity;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Set the current user name for the edit-button visibility converter (Feature 6)
        if (!string.IsNullOrEmpty(viewModel.AgentId))
            IsCurrentUserToVisibilityConverter.CurrentUserName = viewModel.CurrentAgentName;

        // Update converter when agent name is fetched after connect
        // Also update channel subscribe button when selected channel changes
        _onPropertyChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentAgentName) &&
                !string.IsNullOrEmpty(viewModel.CurrentAgentName))
            {
                IsCurrentUserToVisibilityConverter.CurrentUserName = viewModel.CurrentAgentName;
            }
        };

        viewModel.Channels.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChannelsViewModel.SelectedChannel))
            {
                Dispatcher.Invoke(UpdateChannelSubscribeButton);
                WireChannelAutoScroll();
            }
        };
        viewModel.PropertyChanged += _onPropertyChanged;

        // Flash taskbar when new activity arrives and window is not focused
        _onNewActivity = (_, _) =>
        {
            if (!IsActive)
                FlashWindow();
        };
        viewModel.NewActivityArrived += _onNewActivity;

        // Auto-scroll the Log tab ListBox after a batch of entries is flushed
        viewModel.PollingLog.EntriesFlushed += (_, _) =>
        {
            // Defer scroll until after WPF finishes processing the collection changes
            Dispatcher.BeginInvoke(() =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            }, DispatcherPriority.Loaded);
        };

        Closing += (_, _) =>
        {
            viewModel.PropertyChanged -= _onPropertyChanged;
            viewModel.NewActivityArrived -= _onNewActivity;
            viewModel.Shutdown();
        };
    }

    // ── Card expand/collapse on click ───────────────────────────

    private void ActivityCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Don't toggle expand when clicking buttons inside the card
        if (e.OriginalSource is FrameworkElement src && FindParentButton(src) is not null)
            return;

        if (sender is FrameworkElement fe && fe.DataContext is ActivityItem item)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    private void InboxCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ActivityItem item)
        {
            if (!string.IsNullOrEmpty(item.PostId))
            {
                _ = _viewModel.NavigateToPostDiscussionAsync(item.PostId);
                e.Handled = true;
            }
            else
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }
    }

    private void WhatsNewCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WhatsNewEntry entry)
        {
            entry.IsExpanded = !entry.IsExpanded;
        }
    }

    // ── What's New discussion panel ───────────────────────────────

    private void WhatsNewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is WhatsNewEntry entry)
        {
            if (entry.HasPost)
            {
                _ = _viewModel.WhatsNew.LoadDiscussionAsync(entry, CancellationToken.None);
            }
            else
            {
                _viewModel.WhatsNew.CloseDiscussion();
            }
        }
    }

    private void CloseDiscussion_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.WhatsNew.CloseDiscussion();
    }

    private void DiscussionComment_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Don't toggle expand when clicking buttons inside the comment
        if (e.OriginalSource is FrameworkElement src && FindParentButton(src) is not null)
            return;

        if (sender is FrameworkElement fe && fe.DataContext is DiscussionComment comment)
        {
            comment.IsExpanded = !comment.IsExpanded;
            if (comment.IsNew)
            {
                comment.IsNew = false;
                // Decrement channel badge counts if on the Channels tab
                if (_viewModel.SelectedTabIndex == 4 && _viewModel.Channels.SelectedChannel is { } ch)
                {
                    if (ch.NewMessageCount > 0) ch.NewMessageCount--;
                    if (_viewModel.Channels.NewCount > 0) _viewModel.Channels.NewCount--;
                }
            }
        }
    }

    private static System.Windows.Controls.Primitives.ButtonBase? FindParentButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase btn)
                return btn;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    // ── What's New post body expand/collapse ──────────────────────

    private void PostBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.WhatsNew.IsPostBodyExpanded = !_viewModel.WhatsNew.IsPostBodyExpanded;
    }

    private void ReplyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.WhatsNew.IsReplyExpanded = true;
    }

    private void ReplyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.WhatsNew.ReplyText))
            _viewModel.WhatsNew.IsReplyExpanded = false;
    }

    // ── What's New threaded reply-to ────────────────────────────────

    private void ReplyToComment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscussionComment comment)
        {
            // Determine which panel we're in based on the selected tab
            switch (_viewModel.SelectedTabIndex)
            {
                case 1: // Discussions
                    _viewModel.Comments.SetReplyTo(comment);
                    break;
                case 3: // Feed
                    _viewModel.Feed.SetReplyTo(comment);
                    break;
                case 4: // Channels
                    _viewModel.Channels.SetReplyTo(comment);
                    break;
                case 6: // What's New
                    _viewModel.WhatsNew.SetReplyTo(comment);
                    break;
            }
            e.Handled = true;
        }
    }

    private void CancelReplyTo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.WhatsNew.SetReplyTo(null);
    }

    // ── Discussions tab (Feature 1) ─────────────────────────────────

    private void DiscussionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is WatchedDiscussionItem disc)
        {
            _ = _viewModel.Comments.LoadDiscussionAsync(disc, CancellationToken.None);
        }
    }

    private void DiscussionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is WatchedDiscussionItem disc)
        {
            var item = lb.ItemContainerGenerator.ContainerFromItem(disc) as ListBoxItem;
            if (item is not null && item.IsMouseOver)
                _ = _viewModel.Comments.LoadDiscussionAsync(disc, CancellationToken.None);
        }
    }

    private void CloseDiscussionsPanel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Comments.CloseDiscussion();
    }

    private void DiscussionsPostBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.Comments.IsPostBodyExpanded = !_viewModel.Comments.IsPostBodyExpanded;
    }

    private void DiscussionsReplyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.Comments.IsReplyExpanded = true;
    }

    private void DiscussionsReplyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Comments.DiscussionReplyText))
            _viewModel.Comments.IsReplyExpanded = false;
    }

    private void CancelDiscussionsReplyTo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Comments.SetReplyTo(null);
    }

    // ── Feed tab discussion panel (Feature 4) ───────────────────────

    private void FeedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is ActivityItem post)
        {
            _ = _viewModel.Feed.LoadDiscussionAsync(post, CancellationToken.None);
        }
    }

    private void FeedList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // SelectionChanged doesn't fire when re-clicking the already-selected item.
        // Detect that case and reload the discussion anyway.
        if (sender is ListBox lb && lb.SelectedItem is ActivityItem post)
        {
            var item = lb.ItemContainerGenerator.ContainerFromItem(post) as ListBoxItem;
            if (item is not null && item.IsMouseOver)
                _ = _viewModel.Feed.LoadDiscussionAsync(post, CancellationToken.None);
        }
    }

    private void CloseFeedDiscussion_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Feed.CloseDiscussion();
    }

    private void FeedPostBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.Feed.IsPostBodyExpanded = !_viewModel.Feed.IsPostBodyExpanded;
    }

    private void FeedReplyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.Feed.IsReplyExpanded = true;
    }

    private void FeedReplyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Feed.DiscussionReplyText))
            _viewModel.Feed.IsReplyExpanded = false;
    }

    private void CancelFeedReplyTo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Feed.SetReplyTo(null);
    }

    // ── Channel auto-scroll ─────────────────────────────────────────

    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _channelScrollHandler;
    private System.Collections.ObjectModel.ObservableCollection<DiscussionComment>? _channelScrollTarget;

    private void WireChannelAutoScroll()
    {
        // Unwire previous
        if (_channelScrollHandler is not null && _channelScrollTarget is not null)
            _channelScrollTarget.CollectionChanged -= _channelScrollHandler;

        var channel = _viewModel.Channels.SelectedChannel;
        if (channel is null) return;

        _channelScrollTarget = channel.ThreadedMessages;
        _channelScrollHandler = (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ChannelMessagesScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Loaded);
        };
        _channelScrollTarget.CollectionChanged += _channelScrollHandler;

        // Scroll to end on initial selection too
        Dispatcher.BeginInvoke(() => ChannelMessagesScrollViewer.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    // ── Channel reply-to (Feature 8) ────────────────────────────────

    private void CancelChannelReplyTo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Channels.SetReplyTo(null);
    }

    // ── Channel subscribe/unsubscribe ──────────────────────────────

    private void ChannelSubscribe_Click(object sender, RoutedEventArgs e)
    {
        var channel = _viewModel.Channels.SelectedChannel;
        if (channel is null) return;

        if (_viewModel.Channels.IsSubscribed(channel.Id))
            _viewModel.Channels.Unsubscribe(channel.Id);
        else
            _viewModel.Channels.Subscribe(channel.Id);

        UpdateChannelSubscribeButton();
    }

    private void UpdateChannelSubscribeButton()
    {
        var channel = _viewModel.Channels.SelectedChannel;
        if (channel is null) return;

        var isSubscribed = _viewModel.Channels.IsSubscribed(channel.Id);
        ChannelSubscribeBtn.Content = isSubscribed ? "Unsubscribe" : "Subscribe";
        ChannelSubscribeBtn.Background = isSubscribed
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60));
        ChannelSubscribeBtn.Foreground = System.Windows.Media.Brushes.White;
    }

    // ── Agents tab ───────────────────────────────────────────────────

    private void AgentSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.GridViewColumnHeader header && header.Tag is string column)
        {
            _viewModel.Agents.SortBy(column);

            // Update column header text with sort indicators
            UpdateAgentSortHeaders();
        }
    }

    private void UpdateAgentSortHeaders()
    {
        // Headers will show sort arrows via the click handler
        // The sort state is tracked in AgentsViewModel
    }

    private void AgentsRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.Agents.LoadAgentsAsync(CancellationToken.None);
    }

    private void AgentPostList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is AgentPostItem post)
        {
            lb.SelectedItem = null; // Don't keep selection — just navigate
            _viewModel.Agents.OpenPost(post.PostId);
        }
    }

    private void AiAssist_AgentDiscussion_Click(object sender, RoutedEventArgs e)
    {
        RunAiAssist(_viewModel.Agents.NewPostBody, null,
            text => _viewModel.Agents.NewPostBody = text);
    }

    // ── Edit Messages (Feature 6) ───────────────────────────────────

    private void Upvote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ActivityItem item)
            _ = _viewModel.Feed.VoteAsync(item, 1);
    }

    private void Downvote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ActivityItem item)
            _ = _viewModel.Feed.VoteAsync(item, -1);
    }

    private void MessageOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void EditMessage_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Editing messages is not currently supported by the gather.is API.",
            "Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Author Name Click (Feature 9) ───────────────────────────────

    private async void AuthorName_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            string? authorName = null;

            if (fe.DataContext is ActivityItem item)
                authorName = item.Author;
            else if (fe.DataContext is DiscussionComment comment)
                authorName = comment.Author;

            if (string.IsNullOrEmpty(authorName))
                return;

            e.Handled = true;

            var agent = await _viewModel.LookupAgentAsync(authorName);
            if (agent is not null)
            {
                var profileWindow = new UserProfileWindow(agent) { Owner = this };
                profileWindow.ShowDialog();
            }
            else
            {
                MessageBox.Show($"Could not find agent profile for '{authorName}'.",
                    "Agent Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    // ── AI Assist (Feature 7) ───────────────────────────────────────

    private void AiAssist_FeedCompose_Click(object sender, RoutedEventArgs e)
    {
        RunAiAssist(_viewModel.Feed.NewPostBody, null, text => _viewModel.Feed.NewPostBody = text);
    }

    private void AiAssist_FeedReply_Click(object sender, RoutedEventArgs e)
    {
        var context = BuildFeedDiscussionContext();
        RunAiAssist(_viewModel.Feed.DiscussionReplyText, context,
            text => _viewModel.Feed.DiscussionReplyText = text);
    }

    private void AiAssist_DiscussionsReply_Click(object sender, RoutedEventArgs e)
    {
        var context = BuildDiscussionsContext();
        RunAiAssist(_viewModel.Comments.DiscussionReplyText, context,
            text => _viewModel.Comments.DiscussionReplyText = text);
    }

    private void AiAssist_WhatsNewReply_Click(object sender, RoutedEventArgs e)
    {
        var context = BuildWhatsNewContext();
        RunAiAssist(_viewModel.WhatsNew.ReplyText, context,
            text => _viewModel.WhatsNew.ReplyText = text);
    }

    private void AiAssist_Channel_Click(object sender, RoutedEventArgs e)
    {
        var context = BuildChannelContext();
        RunAiAssist(_viewModel.Channels.ReplyText, context,
            text => _viewModel.Channels.ReplyText = text);
    }

    private void RunAiAssist(string originalText, string? context, Action<string> applyResult)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.ClaudeApiKey))
        {
            MessageBox.Show("Claude API key not configured. Add it in Options.",
                "AI Assist", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var claude = new ClaudeApiClient(_viewModel.ClaudeApiKey);
        var dialog = new AiAssistWindow(claude, originalText, context) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            applyResult(dialog.ResultText);
        }
    }

    private string? BuildFeedDiscussionContext()
    {
        if (!_viewModel.Feed.HasDiscussion) return null;
        var sb = new StringBuilder();
        sb.AppendLine($"Post: {_viewModel.Feed.DiscussionTitle}");
        sb.AppendLine(_viewModel.Feed.DiscussionBody);
        foreach (var c in _viewModel.Feed.DiscussionComments.Take(10))
            sb.AppendLine($"{c.Author}: {c.Body}");
        return sb.ToString();
    }

    private string? BuildDiscussionsContext()
    {
        if (!_viewModel.Comments.HasDiscussion) return null;
        var sb = new StringBuilder();
        sb.AppendLine($"Post: {_viewModel.Comments.DiscussionTitle}");
        sb.AppendLine(_viewModel.Comments.DiscussionBody);
        foreach (var c in _viewModel.Comments.DiscussionComments.Take(10))
            sb.AppendLine($"{c.Author}: {c.Body}");
        return sb.ToString();
    }

    private string? BuildWhatsNewContext()
    {
        if (!_viewModel.WhatsNew.HasDiscussion) return null;
        var sb = new StringBuilder();
        sb.AppendLine($"Post: {_viewModel.WhatsNew.DiscussionTitle}");
        sb.AppendLine(_viewModel.WhatsNew.DiscussionBody);
        foreach (var c in _viewModel.WhatsNew.DiscussionComments.Take(10))
            sb.AppendLine($"{c.Author}: {c.Body}");
        return sb.ToString();
    }

    private string? BuildChannelContext()
    {
        if (_viewModel.Channels.SelectedChannel is null) return null;
        var sb = new StringBuilder();
        sb.AppendLine($"Channel: #{_viewModel.Channels.SelectedChannel.Name}");
        foreach (var m in _viewModel.Channels.SelectedChannel.ThreadedMessages.TakeLast(10))
            sb.AppendLine($"{m.Author}: {m.Body}");
        return sb.ToString();
    }

    // ── Taskbar Flash (P/Invoke) ────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private void FlashWindow()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = helper.Handle,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = 3,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("UI: FlashWindow failed", ex);
        }
    }
}

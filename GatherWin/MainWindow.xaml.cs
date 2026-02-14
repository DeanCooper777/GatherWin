using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using GatherWin.Models;
using GatherWin.ViewModels;

namespace GatherWin;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Flash taskbar when new activity arrives and window is not focused
        viewModel.NewActivityArrived += (_, _) =>
        {
            if (!IsActive)
                FlashWindow();
        };

        Closing += (_, _) => viewModel.Shutdown();
    }

    // ── Card expand/collapse on click ───────────────────────────

    private void ActivityCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ActivityItem item)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    /// <summary>
    /// Inbox card header click: if the item references a post, navigate to its discussion.
    /// Otherwise just toggle expand/collapse like other cards.
    /// </summary>
    private void InboxCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ActivityItem item)
        {
            if (!string.IsNullOrEmpty(item.PostId))
            {
                // Navigate to the referenced post's discussion
                _ = _viewModel.NavigateToPostDiscussionAsync(item.PostId);
                e.Handled = true;
            }
            else
            {
                // No post reference — just toggle expand
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
                // Non-post items don't have discussions — close any open panel
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
        // Don't toggle expand when clicking the Reply button
        if (e.OriginalSource is FrameworkElement src && FindParentButton(src) is not null)
            return;

        if (sender is FrameworkElement fe && fe.DataContext is DiscussionComment comment)
        {
            comment.IsExpanded = !comment.IsExpanded;
        }
    }

    /// <summary>Walk up the visual tree to see if the source element is inside a Button.</summary>
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

    // ── Post body expand/collapse ────────────────────────────────

    private void PostBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.WhatsNew.IsPostBodyExpanded = !_viewModel.WhatsNew.IsPostBodyExpanded;
    }

    // ── Reply panel expand/collapse ──────────────────────────────

    private void ReplyTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.WhatsNew.IsReplyExpanded = true;
    }

    private void ReplyTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Only collapse if there's no text being composed
        if (string.IsNullOrWhiteSpace(_viewModel.WhatsNew.ReplyText))
            _viewModel.WhatsNew.IsReplyExpanded = false;
    }

    // ── Threaded reply-to-comment ─────────────────────────────────

    private void ReplyToComment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DiscussionComment comment)
        {
            _viewModel.WhatsNew.SetReplyTo(comment);
            e.Handled = true; // Don't bubble up to card toggle
        }
    }

    private void CancelReplyTo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.WhatsNew.SetReplyTo(null);
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
        catch
        {
            // Ignore flash failures (e.g., handle not available)
        }
    }
}

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class EmailViewModel : ObservableObject
{
    private readonly GatherApiClient _api;
    private readonly HashSet<string> _seenEmailIds = new();

    public ObservableCollection<EmailItem> Emails { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Not yet loaded";
    [ObservableProperty] private EmailItem? _selectedEmail;
    [ObservableProperty] private EmailDetail? _selectedEmailDetail;
    [ObservableProperty] private bool _isLoadingDetail;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private string _directionFilter = "all";
    [ObservableProperty] private bool _unreadOnly;

    // ── Compose ───────────────────────────────────────────────────

    [ObservableProperty] private bool _showCompose;
    [ObservableProperty] private string _composeTo = string.Empty;
    [ObservableProperty] private string _composeSubject = string.Empty;
    [ObservableProperty] private string _composeBody = string.Empty;
    [ObservableProperty] private string? _replyToMessageId;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private string? _sendError;
    [ObservableProperty] private string? _sendSuccess;

    public EmailViewModel(GatherApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedEmailChanged(EmailItem? value)
    {
        SelectedEmailDetail = null;
        if (value is not null)
            _ = LoadEmailDetailAsync(value, CancellationToken.None);
    }

    [RelayCommand]
    public async Task LoadEmailsAsync(CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Loading emails...";

        try
        {
            var direction = DirectionFilter == "all" ? null : DirectionFilter;
            var response = await _api.GetEmailsAsync(ct, direction: direction, unreadOnly: UnreadOnly);
            if (response?.Emails is null)
            {
                StatusText = "No emails found";
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                Emails.Clear();
                foreach (var email in response.Emails)
                    Emails.Add(email);
            });

            StatusText = $"{response.Emails.Count} email(s) — {response.Unread} unread";
            AppLogger.Log("Email", $"Loaded {response.Emails.Count} emails ({response.Unread} unread)");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLogger.LogError("Email: load failed", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private async Task LoadEmailDetailAsync(EmailItem email, CancellationToken ct)
    {
        IsLoadingDetail = true;
        try
        {
            var detail = await _api.GetEmailDetailAsync(email.Id ?? "", ct);
            Application.Current.Dispatcher.Invoke(() => SelectedEmailDetail = detail);

            // Auto mark as read
            if (!email.Read)
            {
                await _api.MarkEmailReadAsync(email.Id ?? "", ct);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    email.Read = true;
                    OnPropertyChanged(nameof(Emails));
                });
            }
        }
        catch (Exception ex) { AppLogger.LogError("Email: load detail failed", ex); }
        finally { IsLoadingDetail = false; }
    }

    [RelayCommand]
    private void StartReply()
    {
        if (SelectedEmailDetail is null) return;
        ShowCompose = true;
        ComposeTo = SelectedEmailDetail.FromAddr ?? string.Empty;
        ComposeSubject = SelectedEmailDetail.Subject?.StartsWith("Re:") == true
            ? SelectedEmailDetail.Subject
            : $"Re: {SelectedEmailDetail.Subject}";
        ReplyToMessageId = SelectedEmailDetail.MessageId;
        SendError = null;
        SendSuccess = null;
    }

    [RelayCommand]
    private void StartNewEmail()
    {
        ShowCompose = true;
        ComposeTo = string.Empty;
        ComposeSubject = string.Empty;
        ComposeBody = string.Empty;
        ReplyToMessageId = null;
        SendError = null;
        SendSuccess = null;
    }

    [RelayCommand]
    private void CancelCompose()
    {
        ShowCompose = false;
        ComposeTo = string.Empty;
        ComposeSubject = string.Empty;
        ComposeBody = string.Empty;
        ReplyToMessageId = null;
        SendError = null;
    }

    [RelayCommand]
    private async Task SendEmailAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ComposeTo))
        {
            SendError = "Recipient is required";
            return;
        }
        if (string.IsNullOrWhiteSpace(ComposeSubject))
        {
            SendError = "Subject is required";
            return;
        }
        if (string.IsNullOrWhiteSpace(ComposeBody))
        {
            SendError = "Body is required";
            return;
        }

        IsSending = true;
        SendError = null;
        SendSuccess = null;

        try
        {
            // Convert plain text to basic HTML
            var html = string.Join("<br>",
                ComposeBody.Split('\n').Select(System.Net.WebUtility.HtmlEncode));

            var (success, error) = await _api.SendEmailAsync(
                ComposeTo.Trim(), ComposeSubject.Trim(), html, ReplyToMessageId, ct);

            if (success)
            {
                SendSuccess = $"Email sent to {ComposeTo}";
                CancelCompose();
                AppLogger.Log("Email", $"Sent email to {ComposeTo}: {ComposeSubject}");
                await LoadEmailsAsync(ct);
            }
            else
            {
                SendError = error ?? "Failed to send email";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Email: send failed", ex);
            SendError = ex.Message;
        }
        finally { IsSending = false; }
    }

    /// <summary>Called by polling to notify new inbound emails.</summary>
    public void OnNewEmailsReceived(IEnumerable<EmailItem> newEmails)
    {
        var added = 0;
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var email in newEmails)
            {
                if (_seenEmailIds.Add(email.Id ?? ""))
                {
                    Emails.Insert(0, email);
                    if (!email.Read) added++;
                }
            }
        });
        if (added > 0)
        {
            NewCount += added;
            StatusText = $"{Emails.Count} email(s) — {NewCount} new";
        }
    }

    public void SeedSeenIds(IEnumerable<string> ids)
    {
        foreach (var id in ids) _seenEmailIds.Add(id);
    }

    public void ResetNewCount() => NewCount = 0;
}

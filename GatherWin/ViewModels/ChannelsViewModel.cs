using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class ChannelInfo : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private int _memberCount;
    [ObservableProperty] private int _newMessageCount;

    public ObservableCollection<ActivityItem> Messages { get; } = new();
}

public partial class ChannelsViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<ChannelInfo> Channels { get; } = new();

    [ObservableProperty] private ChannelInfo? _selectedChannel;
    [ObservableProperty] private string _replyText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private string? _sendError;

    // ── Channel Creation State ──────────────────────────────────
    [ObservableProperty] private bool _isCreatingChannel;
    [ObservableProperty] private string _newChannelName = string.Empty;
    [ObservableProperty] private string _newChannelDescription = string.Empty;
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private string? _createError;

    public ChannelsViewModel(GatherApiClient api)
    {
        _api = api;
    }

    public void AddMessage(string channelId, string channelName, string author, string body, DateTimeOffset timestamp, bool isNew = true)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Find or create channel
            var channel = Channels.FirstOrDefault(c => c.Id == channelId);
            if (channel is null)
            {
                channel = new ChannelInfo { Id = channelId, Name = channelName };
                Channels.Add(channel);
            }

            var item = new ActivityItem
            {
                Type = ActivityType.Channel,
                Id = Guid.NewGuid().ToString(),
                Title = channelName,
                Author = author,
                Body = body,
                Timestamp = timestamp,
                ChannelId = channelId,
                ChannelName = channelName,
                IsNew = isNew
            };

            InsertSorted(channel.Messages, item);
            if (isNew)
            {
                channel.NewMessageCount++;
                NewCount++;
            }
        });
    }

    public void ResetNewCount() => NewCount = 0;

    private static void InsertSorted(ObservableCollection<ActivityItem> list, ActivityItem item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (item.Timestamp >= list[i].Timestamp)
            {
                list.Insert(i, item);
                return;
            }
        }
        list.Add(item);
    }

    [RelayCommand]
    private async Task SendMessageAsync(CancellationToken ct)
    {
        if (SelectedChannel is null || string.IsNullOrWhiteSpace(ReplyText))
            return;

        IsSending = true;
        SendError = null;

        try
        {
            var (success, error) = await _api.PostChannelMessageAsync(
                SelectedChannel.Id, ReplyText.Trim(), ct);

            if (success)
            {
                var msg = new ActivityItem
                {
                    Type = ActivityType.Channel,
                    Id = Guid.NewGuid().ToString(),
                    Title = SelectedChannel.Name,
                    Author = "OnTheEdgeOfReality",
                    Body = ReplyText.Trim(),
                    Timestamp = DateTimeOffset.Now,
                    ChannelId = SelectedChannel.Id,
                    ChannelName = SelectedChannel.Name,
                    IsNew = false
                };

                Application.Current.Dispatcher.Invoke(() =>
                    SelectedChannel.Messages.Insert(0, msg));

                ReplyText = string.Empty;
            }
            else
            {
                SendError = error ?? "Failed to send message";
            }
        }
        catch (Exception ex)
        {
            SendError = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }

    // ── Channel Creation ────────────────────────────────────────

    /// <summary>Show the "create channel" inline form.</summary>
    [RelayCommand]
    private void ShowCreateChannel()
    {
        IsCreatingChannel = true;
        NewChannelName = string.Empty;
        NewChannelDescription = string.Empty;
        CreateError = null;
    }

    /// <summary>Hide the "create channel" inline form.</summary>
    [RelayCommand]
    private void CancelCreateChannel()
    {
        IsCreatingChannel = false;
        NewChannelName = string.Empty;
        NewChannelDescription = string.Empty;
        CreateError = null;
    }

    /// <summary>Create the channel via API and add it to the list.</summary>
    [RelayCommand]
    private async Task CreateChannelAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewChannelName))
        {
            CreateError = "Channel name is required";
            return;
        }

        IsCreating = true;
        CreateError = null;

        try
        {
            var description = string.IsNullOrWhiteSpace(NewChannelDescription)
                ? null : NewChannelDescription.Trim();

            var (success, channelId, error) = await _api.CreateChannelAsync(
                NewChannelName.Trim(), description, ct);

            if (success)
            {
                AppLogger.Log("Channels", $"Created channel '{NewChannelName.Trim()}' (id={channelId})");

                var newChannel = new ChannelInfo
                {
                    Id = channelId ?? Guid.NewGuid().ToString(),
                    Name = NewChannelName.Trim(),
                    Description = description ?? string.Empty,
                    MemberCount = 1
                };

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Channels.Add(newChannel);
                    SelectedChannel = newChannel;
                });

                IsCreatingChannel = false;
                NewChannelName = string.Empty;
                NewChannelDescription = string.Empty;
            }
            else
            {
                CreateError = error ?? "Failed to create channel";
            }
        }
        catch (Exception ex)
        {
            CreateError = ex.Message;
        }
        finally
        {
            IsCreating = false;
        }
    }
}

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GatherWin.Models;
using GatherWin.Services;

namespace GatherWin.ViewModels;

public partial class ShopViewModel : ObservableObject
{
    private readonly GatherApiClient _api;

    public ObservableCollection<MenuCategory> Categories { get; } = new();
    public ObservableCollection<MenuItem> Items { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Not yet loaded";
    [ObservableProperty] private MenuCategory? _selectedCategory;
    [ObservableProperty] private bool _isLoadingItems;
    [ObservableProperty] private string _itemsStatus = string.Empty;

    /// <summary>BCH to USD exchange rate for price display.</summary>
    public decimal BchToUsdRate { get; set; }

    public ShopViewModel(GatherApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedCategoryChanged(MenuCategory? value)
    {
        if (value is not null)
            _ = LoadCategoryItemsAsync(value, CancellationToken.None);
        else
        {
            Items.Clear();
            ItemsStatus = string.Empty;
        }
    }

    [RelayCommand]
    public async Task LoadCategoriesAsync(CancellationToken ct)
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Loading shop...";

        try
        {
            var response = await _api.GetMenuAsync(ct);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Categories.Clear();
                if (response?.Categories is not null)
                    foreach (var cat in response.Categories)
                        Categories.Add(cat);
            });

            StatusText = Categories.Count > 0
                ? $"{Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}"
                : "Shop is empty";

            // Auto-select first category
            if (Categories.Count > 0 && SelectedCategory is null)
                SelectedCategory = Categories[0];
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Shop: load categories failed", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCategoryItemsAsync(MenuCategory category, CancellationToken ct)
    {
        IsLoadingItems = true;
        ItemsStatus = $"Loading {category.Name}...";

        try
        {
            var response = await _api.GetMenuCategoryAsync(category.Id ?? "", ct);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Items.Clear();
                if (response?.Items is not null)
                    foreach (var item in response.Items)
                        Items.Add(item);
            });

            ItemsStatus = Items.Count > 0
                ? $"{Items.Count} item(s) in {category.Name}"
                : "No items in this category";
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Shop: load items for {category.Id} failed", ex);
            ItemsStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingItems = false;
        }
    }

    public string FormatPrice(string? bchPrice)
    {
        if (string.IsNullOrEmpty(bchPrice)) return "N/A";
        var display = $"{bchPrice} BCH";
        if (decimal.TryParse(bchPrice, out var bch) && bch > 0 && BchToUsdRate > 0)
            display += $" (~${bch * BchToUsdRate:F2} USD)";
        return display;
    }
}

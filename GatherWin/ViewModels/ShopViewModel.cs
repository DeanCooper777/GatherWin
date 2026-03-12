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

    // ── Product Options & Order Flow ─────────────────────────────

    [ObservableProperty] private MenuItem? _selectedItem;
    [ObservableProperty] private ProductOptionsResponse? _productOptions;
    [ObservableProperty] private bool _isLoadingOptions;

    public ObservableCollection<ProductOptionGroup> OptionGroups { get; } = new();

    partial void OnProductOptionsChanged(ProductOptionsResponse? value)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            OptionGroups.Clear();
            if (value?.Options is null) return;
            foreach (var (key, choices) in value.Options)
                OptionGroups.Add(new ProductOptionGroup(this) { Key = key, Choices = choices });
        });
    }

    // Order form
    [ObservableProperty] private bool _showOrderForm;
    [ObservableProperty] private Dictionary<string, string> _selectedOptions = new();
    [ObservableProperty] private string _orderFirstName = string.Empty;
    [ObservableProperty] private string _orderLastName = string.Empty;
    [ObservableProperty] private string _orderAddress1 = string.Empty;
    [ObservableProperty] private string _orderAddress2 = string.Empty;
    [ObservableProperty] private string _orderCity = string.Empty;
    [ObservableProperty] private string _orderState = string.Empty;
    [ObservableProperty] private string _orderPostCode = string.Empty;
    [ObservableProperty] private string _orderCountry = "US";
    [ObservableProperty] private string _orderEmail = string.Empty;
    [ObservableProperty] private bool _isPlacingOrder;
    [ObservableProperty] private string? _orderError;

    // Active order tracking
    [ObservableProperty] private bool _showOrderStatus;
    [ObservableProperty] private OrderCreateResponse? _activeOrder;
    [ObservableProperty] private OrderStatusResponse? _orderStatus;
    [ObservableProperty] private string _payTxId = string.Empty;
    [ObservableProperty] private bool _isPayingOrder;
    [ObservableProperty] private bool _isRefreshingStatus;
    [ObservableProperty] private string? _payError;
    [ObservableProperty] private string? _paySuccess;

    partial void OnSelectedItemChanged(MenuItem? value)
    {
        ProductOptions = null;
        SelectedOptions = new Dictionary<string, string>();
        if (value is not null && value.Available)
            _ = LoadProductOptionsAsync(value, CancellationToken.None);
    }

    private async Task LoadProductOptionsAsync(MenuItem item, CancellationToken ct)
    {
        IsLoadingOptions = true;
        try
        {
            var opts = await _api.GetProductOptionsAsync(item.Id ?? "", ct);
            Application.Current.Dispatcher.Invoke(() => ProductOptions = opts);
            AppLogger.Log("Shop", $"Loaded options for {item.Name}: {opts?.Options?.Count ?? 0} option type(s)");
        }
        catch (Exception ex) { AppLogger.LogError($"Shop: load options for {item.Id} failed", ex); }
        finally { IsLoadingOptions = false; }
    }

    [RelayCommand]
    private void OpenOrderForm()
    {
        if (SelectedItem is null || !SelectedItem.Available) return;
        ShowOrderForm = true;
        OrderError = null;
    }

    [RelayCommand]
    private void CancelOrderForm()
    {
        ShowOrderForm = false;
        OrderError = null;
    }

    [RelayCommand]
    private async Task PlaceOrderAsync(CancellationToken ct)
    {
        if (SelectedItem is null) return;
        if (string.IsNullOrWhiteSpace(OrderFirstName) || string.IsNullOrWhiteSpace(OrderLastName) ||
            string.IsNullOrWhiteSpace(OrderAddress1) || string.IsNullOrWhiteSpace(OrderCity) ||
            string.IsNullOrWhiteSpace(OrderPostCode) || string.IsNullOrWhiteSpace(OrderCountry) ||
            string.IsNullOrWhiteSpace(OrderEmail))
        {
            OrderError = "All required shipping fields must be filled in";
            return;
        }

        IsPlacingOrder = true;
        OrderError = null;

        try
        {
            var order = new ProductOrderRequest
            {
                ProductId = SelectedItem.Id,
                Options = new Dictionary<string, string>(SelectedOptions),
                ShippingAddress = new ShippingAddress
                {
                    FirstName = OrderFirstName.Trim(),
                    LastName = OrderLastName.Trim(),
                    AddressLine1 = OrderAddress1.Trim(),
                    AddressLine2 = string.IsNullOrWhiteSpace(OrderAddress2) ? null : OrderAddress2.Trim(),
                    City = OrderCity.Trim(),
                    State = string.IsNullOrWhiteSpace(OrderState) ? null : OrderState.Trim(),
                    PostCode = OrderPostCode.Trim(),
                    Country = OrderCountry.Trim().ToUpperInvariant(),
                    Email = OrderEmail.Trim(),
                }
            };

            var (success, result, error) = await _api.CreateOrderAsync(order, ct);
            if (success && result is not null)
            {
                ActiveOrder = result;
                ShowOrderForm = false;
                ShowOrderStatus = true;
                AppLogger.Log("Shop", $"Order created: {result.OrderId} — {result.TotalBch} BCH to {result.PaymentAddress}");
            }
            else
            {
                OrderError = error ?? "Failed to place order";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Shop: place order failed", ex);
            OrderError = ex.Message;
        }
        finally { IsPlacingOrder = false; }
    }

    [RelayCommand]
    private async Task SubmitPaymentAsync(CancellationToken ct)
    {
        var txId = PayTxId.Trim();
        if (txId.Length != 64)
        {
            PayError = "BCH tx_id must be exactly 64 hex characters";
            return;
        }
        if (ActiveOrder?.OrderId is null) return;

        IsPayingOrder = true;
        PayError = null;
        PaySuccess = null;

        try
        {
            var (success, result, error) = await _api.PayOrderAsync(ActiveOrder.OrderId, txId, ct);
            if (success)
            {
                PaySuccess = $"Payment submitted! Status: {result?.Status ?? "processing"}";
                PayTxId = string.Empty;
                AppLogger.Log("Shop", $"Payment submitted for order {ActiveOrder.OrderId}");
                await RefreshOrderStatusAsync(ct);
            }
            else
            {
                PayError = error ?? "Payment submission failed";
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Shop: submit payment failed", ex);
            PayError = ex.Message;
        }
        finally { IsPayingOrder = false; }
    }

    [RelayCommand]
    private async Task RefreshOrderStatusAsync(CancellationToken ct)
    {
        if (ActiveOrder?.OrderId is null) return;
        IsRefreshingStatus = true;
        try
        {
            var status = await _api.GetOrderStatusAsync(ActiveOrder.OrderId, ct);
            Application.Current.Dispatcher.Invoke(() => OrderStatus = status);
        }
        catch (Exception ex) { AppLogger.LogError("Shop: refresh order status failed", ex); }
        finally { IsRefreshingStatus = false; }
    }

    [RelayCommand]
    private void CloseOrderStatus()
    {
        ShowOrderStatus = false;
        ActiveOrder = null;
        OrderStatus = null;
        PayTxId = string.Empty;
        PayError = null;
        PaySuccess = null;
    }

    public void SetOption(string key, string value)
    {
        SelectedOptions[key] = value;
        OnPropertyChanged(nameof(SelectedOptions));
    }
}

public partial class ProductOptionGroup : ObservableObject
{
    private readonly ShopViewModel _parent;
    public string Key { get; init; } = string.Empty;
    public List<string> Choices { get; init; } = new();

    [ObservableProperty] private string? _selectedChoice;

    public ProductOptionGroup(ShopViewModel parent) { _parent = parent; }

    partial void OnSelectedChoiceChanged(string? value)
    {
        if (value is not null)
            _parent.SetOption(Key, value);
    }
}

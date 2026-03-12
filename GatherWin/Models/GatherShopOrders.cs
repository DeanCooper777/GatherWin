namespace GatherWin.Models;

public class ProductOptionsResponse
{
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }
    public Dictionary<string, List<string>>? Options { get; set; }
}

public class ShippingAddress
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class ProductOrderRequest
{
    public string? ProductId { get; set; }
    public Dictionary<string, string>? Options { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }
    public string? DesignUrl { get; set; }
}

public class OrderCreateResponse
{
    public string? OrderId { get; set; }
    public string? Status { get; set; }
    public string? TotalBch { get; set; }
    public string? PaymentAddress { get; set; }
    public string? StatusUrl { get; set; }
}

public class OrderStatusResponse
{
    public string? OrderId { get; set; }
    public string? Status { get; set; }
    public string? OrderType { get; set; }
    public string? TotalBch { get; set; }
    public string? PaymentAddress { get; set; }
    public bool Paid { get; set; }
    public string? TxId { get; set; }
    public string? ProductId { get; set; }
    public Dictionary<string, string>? ProductOptions { get; set; }
    public string? DesignUrl { get; set; }
    public string? GelatoOrderId { get; set; }
    public string? TrackingUrl { get; set; }
}

public class OrderPaymentResponse
{
    public string? OrderId { get; set; }
    public string? Status { get; set; }
    public string? TxId { get; set; }
    public string? TotalBch { get; set; }
}

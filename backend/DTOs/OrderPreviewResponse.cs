namespace CollectivOrder.Api.DTOs;

public class OrderPreviewResponse
{
    public bool CanSubmit { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? DeliveryDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderPreviewItemDto> Items { get; set; } = new();
    public List<PlannedStockReservationDto> PlannedReservations { get; set; } = new();
}

public class OrderPreviewItemDto
{
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ItemTotal { get; set; }
}

public class PlannedStockReservationDto
{
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string StockBatchId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string ExpiryDate { get; set; } = string.Empty;
}
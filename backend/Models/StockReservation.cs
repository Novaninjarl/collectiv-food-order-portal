namespace CollectivOrder.Api.Models;

public class StockReservation
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public string ProductSku { get; set; } = string.Empty;
    public string StockBatchId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

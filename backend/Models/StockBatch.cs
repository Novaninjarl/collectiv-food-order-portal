using System.ComponentModel.DataAnnotations;

namespace CollectivOrder.Api.Models;

public class StockBatch
{
    [Key]
    public string BatchId { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public int AvailableQuantity { get; set; }
    public DateTime ExpiryDate { get; set; }
}

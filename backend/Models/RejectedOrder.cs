namespace CollectivOrder.Api.Models;

public class RejectedOrder
{
    public int Id { get; set; }
    public int? SourceOrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ReasonsJson { get; set; } = "[]";
    public string RawJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

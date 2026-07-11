namespace CollectivOrder.Api.Models;

public class EventLog
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public int? CustomerId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

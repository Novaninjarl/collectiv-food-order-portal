using System.Text.Json;
using CollectivOrder.Api.Data;
using CollectivOrder.Api.Models;

namespace CollectivOrder.Api.Services;

public class EventLogService
{
    private readonly AppDbContext _db;

    public EventLogService(AppDbContext db)
    {
        _db = db;
    }

    public void Add(string type, string message, int? orderId = null, int? customerId = null, object? metadata = null)
    {
        _db.EventLogs.Add(new EventLog
        {
            Type = type,
            OrderId = orderId,
            CustomerId = customerId,
            Message = message,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata),
            CreatedAt = DateTime.UtcNow
        });
    }
}

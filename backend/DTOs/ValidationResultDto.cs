using CollectivOrder.Api.Models;

namespace CollectivOrder.Api.DTOs;

public class ValidatedOrderDraft
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public AddressDto DeliveryAddress { get; set; } = new();
    public DateTime DeliveryDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderValidationResult
{
    public bool IsValid => Reasons.Count == 0 && Draft is not null;
    public List<string> Reasons { get; set; } = new();
    public ValidatedOrderDraft? Draft { get; set; }
}

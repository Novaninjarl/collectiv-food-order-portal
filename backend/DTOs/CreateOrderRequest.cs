namespace CollectivOrder.Api.DTOs;

public class CreateOrderRequest
{
    public string? CustomerName { get; set; }
    public AddressDto? DeliveryAddress { get; set; }
    public string? DeliveryDate { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class CreateOrderItemRequest
{
    public string? ProductSku { get; set; }
    public int Quantity { get; set; }
    public decimal ItemTotal { get; set; }
}

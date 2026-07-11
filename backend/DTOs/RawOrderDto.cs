namespace CollectivOrder.Api.DTOs;

public class RawOrderDto
{
    public int Id { get; set; }
    public string? CustomerName { get; set; }
    public AddressDto? DeliveryAddress { get; set; }
    public string? DeliveryDate { get; set; }
    public List<RawOrderItemDto>? Items { get; set; }
    public decimal Total { get; set; }
}

public class RawOrderItemDto
{
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal ItemTotal { get; set; }
}

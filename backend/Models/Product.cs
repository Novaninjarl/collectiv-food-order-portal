using System.ComponentModel.DataAnnotations;

namespace CollectivOrder.Api.Models;

public class Product
{
    [Key]
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

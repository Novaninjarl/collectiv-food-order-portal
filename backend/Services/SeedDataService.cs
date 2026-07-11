using System.Globalization;
using System.Text.Json;
using CollectivOrder.Api.Data;
using CollectivOrder.Api.DTOs;
using CollectivOrder.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectivOrder.Api.Services;

public class SeedDataService
{
    private readonly AppDbContext _db;
    private readonly OrderValidationService _orderValidation;
    private readonly StockAllocationService _stockAllocation;
    private readonly CustomerService _customerService;
    private readonly EventLogService _events;

    public SeedDataService(
        AppDbContext db,
        OrderValidationService orderValidation,
        StockAllocationService stockAllocation,
        CustomerService customerService,
        EventLogService events)
    {
        _db = db;
        _orderValidation = orderValidation;
        _stockAllocation = stockAllocation;
        _customerService = customerService;
        _events = events;
    }

    public async Task SeedAsync()
    {
        await _db.Database.EnsureCreatedAsync();

        if (!await _db.Products.AnyAsync())
        {
            await SeedProductsAsync();
        }

        if (!await _db.StockBatches.AnyAsync())
        {
            await SeedStockBatchesAsync();
        }

        if (!await _db.Orders.AnyAsync() && !await _db.RejectedOrders.AnyAsync())
        {
            await ImportOrdersAsync();
        }
    }

    private async Task SeedProductsAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "products.json");
        var json = await File.ReadAllTextAsync(path);
        var products = JsonSerializer.Deserialize<List<ProductSeedDto>>(json, JsonOptions()) ?? new();

        foreach (var product in products)
        {
            _db.Products.Add(new Product
            {
                Sku = OrderValidationService.NormaliseSku(product.Sku),
                Name = product.Name.Trim()
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SeedStockBatchesAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "stockBatches.json");
        var json = await File.ReadAllTextAsync(path);
        var batches = JsonSerializer.Deserialize<List<StockBatchSeedDto>>(json, JsonOptions()) ?? new();

        foreach (var batch in batches)
        {
            if (!DateTime.TryParseExact(batch.ExpiryDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiryDate))
            {
                continue;
            }

            _db.StockBatches.Add(new StockBatch
            {
                BatchId = batch.BatchId.Trim(),
                ProductSku = OrderValidationService.NormaliseSku(batch.ProductSku),
                AvailableQuantity = batch.AvailableQuantity,
                ExpiryDate = expiryDate
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task ImportOrdersAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "orders.json");
        var json = await File.ReadAllTextAsync(path);
        var rawOrders = JsonSerializer.Deserialize<List<RawOrderDto>>(json, JsonOptions()) ?? new();
        var productsBySku = await _db.Products.ToDictionaryAsync(p => p.Sku, p => p);
        var existingIds = new HashSet<int>();

        foreach (var rawOrder in rawOrders)
        {
            var validation = _orderValidation.Validate(rawOrder, productsBySku, existingIds);
            if (!validation.IsValid || validation.Draft is null)
            {
                AddRejected(rawOrder, validation.Reasons);
                _events.Add("ORDER_REJECTED", $"Rejected imported order {rawOrder.Id}: {string.Join("; ", validation.Reasons)}", rawOrder.Id);
                await _db.SaveChangesAsync();
                continue;
            }

            var draft = validation.Draft;
            var customer = await _customerService.FindOrCreateAsync(draft.CustomerName, draft.DeliveryAddress);
            var order = CreateOrderFromDraft(draft, customer.Id);

            var stockResult = await _stockAllocation.ReserveAsync(order.Id, order.DeliveryDate, order.Items);
            if (!stockResult.Success)
            {
                AddRejected(rawOrder, stockResult.Reasons);
                _events.Add("ORDER_REJECTED", $"Rejected imported order {rawOrder.Id}: {string.Join("; ", stockResult.Reasons)}", rawOrder.Id, customer.Id);
                await _db.SaveChangesAsync();
                continue;
            }

            order.StockReservations = stockResult.Reservations;
            _db.Orders.Add(order);
            existingIds.Add(order.Id);
            _events.Add("ORDER_IMPORTED", $"Imported valid order {order.Id} for {order.CustomerName}.", order.Id, customer.Id);
            _events.Add("STOCK_RESERVED", $"Reserved stock for imported order {order.Id} using earliest-expiring valid batches.", order.Id, customer.Id, stockResult.Reservations.Select(r => new { r.ProductSku, r.StockBatchId, r.Quantity }));
            await _db.SaveChangesAsync();
        }
    }

    private void AddRejected(RawOrderDto rawOrder, List<string> reasons)
    {
        _db.RejectedOrders.Add(new RejectedOrder
        {
            SourceOrderId = rawOrder.Id > 0 ? rawOrder.Id : null,
            CustomerName = rawOrder.CustomerName?.Trim() ?? string.Empty,
            ReasonsJson = JsonSerializer.Serialize(reasons),
            RawJson = JsonSerializer.Serialize(rawOrder),
            CreatedAt = DateTime.UtcNow
        });
    }

    public static Order CreateOrderFromDraft(ValidatedOrderDraft draft, int customerId)
    {
        return new Order
        {
            Id = draft.Id,
            CustomerId = customerId,
            CustomerName = draft.CustomerName,
            AddressLine1 = draft.DeliveryAddress.AddressLine1 ?? string.Empty,
            Postcode = draft.DeliveryAddress.Postcode ?? string.Empty,
            City = draft.DeliveryAddress.City ?? string.Empty,
            DeliveryDate = draft.DeliveryDate,
            Total = draft.Total,
            Items = draft.Items,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private class ProductSeedDto
    {
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private class StockBatchSeedDto
    {
        public string BatchId { get; set; } = string.Empty;
        public string ProductSku { get; set; } = string.Empty;
        public int AvailableQuantity { get; set; }
        public string ExpiryDate { get; set; } = string.Empty;
    }
}

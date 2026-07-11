using System.Text.Json;
using CollectivOrder.Api.Data;
using CollectivOrder.Api.DTOs;
using CollectivOrder.Api.Models;
using CollectivOrder.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=collectiv-orders-v3.db"));

builder.Services.AddScoped<OrderValidationService>();
builder.Services.AddScoped<StockAllocationService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<EventLogService>();
builder.Services.AddScoped<SeedDataService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("frontend");

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SeedDataService>().SeedAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/products", async (AppDbContext db) =>
{
    var products = await db.Products.OrderBy(p => p.Name).ToListAsync();
    return Results.Ok(products);
});

app.MapGet("/api/recommendations", async (string? query, int? customerId, int? limit, AppDbContext db) =>
{
    var products = await db.Products.OrderBy(p => p.Name).ToListAsync();

    var popularity = await db.OrderItems
        .GroupBy(i => i.ProductSku)
        .Select(g => new { Sku = g.Key, Count = g.Sum(i => i.Quantity) })
        .ToDictionaryAsync(x => x.Sku, x => x.Count);

    var averageUnitPrices = await db.OrderItems
        .Where(i => i.Quantity > 0)
        .GroupBy(i => i.ProductSku)
        .Select(g => new { Sku = g.Key, UnitPrice = g.Average(i => (double)i.ItemTotal / i.Quantity) })
        .ToDictionaryAsync(x => x.Sku, x => Math.Round((decimal)x.UnitPrice, 2));

    var customerHistory = new Dictionary<string, int>();
    if (customerId is not null)
    {
        customerHistory = await db.Orders
            .Where(o => o.CustomerId == customerId)
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductSku)
            .Select(g => new { Sku = g.Key, Count = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Sku, x => x.Count);
    }

    var recommendations = BuildRecommendations(
        products,
        query ?? string.Empty,
        customerHistory,
        popularity,
        averageUnitPrices,
        Math.Clamp(limit ?? 6, 1, 10));

    return Results.Ok(recommendations);
});

app.MapGet("/api/stock-batches", async (AppDbContext db) =>
{
    var batches = await db.StockBatches
        .OrderBy(b => b.ProductSku)
        .ThenBy(b => b.ExpiryDate)
        .ToListAsync();
    return Results.Ok(batches);
});

app.MapGet("/api/orders", async (AppDbContext db) =>
{
    var orders = await db.Orders
        .Include(o => o.Customer)
        .Include(o => o.Items)
        .Include(o => o.StockReservations)
        .OrderBy(o => o.DeliveryDate)
        .ThenBy(o => o.Id)
        .Select(o => new
        {
            o.Id,
            o.CustomerId,
            o.CustomerName,
            deliveryAddress = new { o.AddressLine1, o.Postcode, o.City },
            deliveryDate = o.DeliveryDate.ToString("dd/MM/yyyy"),
            o.Total,
            items = o.Items.Select(i => new { i.ProductSku, i.ProductName, i.Quantity, i.ItemTotal }),
            stockReservations = o.StockReservations.Select(r => new { r.ProductSku, r.StockBatchId, r.Quantity })
        })
        .ToListAsync();

    return Results.Ok(orders);
});

app.MapGet("/api/rejected-orders", async (AppDbContext db) =>
{
    var rejected = await db.RejectedOrders
        .OrderBy(r => r.SourceOrderId)
        .ThenBy(r => r.Id)
        .ToListAsync();

    return Results.Ok(rejected.Select(r => new
    {
        r.Id,
        r.SourceOrderId,
        r.CustomerName,
        reasons = DeserializeReasons(r.ReasonsJson),
        r.CreatedAt
    }));
});

app.MapGet("/api/customers", async (AppDbContext db) =>
{
    var customers = await db.Customers
        .Include(c => c.Orders)
        .OrderBy(c => c.Name)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.AddressLine1,
            c.Postcode,
            c.City,
            orderCount = c.Orders.Count,
            lastOrderDate = c.Orders.OrderByDescending(o => o.DeliveryDate).Select(o => o.DeliveryDate.ToString("dd/MM/yyyy")).FirstOrDefault()
        })
        .ToListAsync();

    return Results.Ok(customers);
});


app.MapGet("/api/me", async (HttpRequest request, AppDbContext db) =>
{
    var customer = await GetCustomerFromHeaderAsync(request, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    var orderCount = await db.Orders.CountAsync(o => o.CustomerId == customer.Id);
    var lastOrderDate = await db.Orders
        .Where(o => o.CustomerId == customer.Id)
        .OrderByDescending(o => o.DeliveryDate)
        .Select(o => o.DeliveryDate.ToString("dd/MM/yyyy"))
        .FirstOrDefaultAsync();

    return Results.Ok(new
    {
        customer.Id,
        customer.Name,
        customer.AddressLine1,
        customer.Postcode,
        customer.City,
        orderCount,
        lastOrderDate
    });
});


app.MapPost("/api/me/orders/preview", async (
    HttpRequest httpRequest,
    CreateOrderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock) =>
{
    var customer = await GetCustomerFromHeaderAsync(httpRequest, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    var scopedRequest = new CreateOrderRequest
    {
        CustomerName = customer.Name,
        DeliveryAddress = new AddressDto
        {
            AddressLine1 = customer.AddressLine1,
            Postcode = customer.Postcode,
            City = customer.City
        },
        DeliveryDate = request.DeliveryDate,
        Items = request.Items
    };

    return await PreviewOrderAsync(scopedRequest, db, validation, stock);
});

app.MapPost("/api/orders/preview", async (
    CreateOrderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock) =>
{
    return await PreviewOrderAsync(request, db, validation, stock);
});

app.MapGet("/api/me/orders", async (HttpRequest request, AppDbContext db) =>
{
    var customer = await GetCustomerFromHeaderAsync(request, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    var orders = await db.Orders
        .Where(o => o.CustomerId == customer.Id)
        .Include(o => o.Items)
        .Include(o => o.StockReservations)
        .OrderByDescending(o => o.DeliveryDate)
        .ThenByDescending(o => o.Id)
        .Select(o => new
        {
            o.Id,
            o.CustomerId,
            o.CustomerName,
            deliveryAddress = new { o.AddressLine1, o.Postcode, o.City },
            deliveryDate = o.DeliveryDate.ToString("dd/MM/yyyy"),
            o.Total,
            items = o.Items.Select(i => new { i.ProductSku, i.ProductName, i.Quantity, i.ItemTotal }),
            stockReservations = o.StockReservations.Select(r => new { r.ProductSku, r.StockBatchId, r.Quantity })
        })
        .ToListAsync();

    return Results.Ok(orders);
});

app.MapGet("/api/me/rejected-orders", async (HttpRequest request, AppDbContext db) =>
{
    var customer = await GetCustomerFromHeaderAsync(request, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    // The seed data has no stable customer ID for rejected rows, so for the mock customer portal
    // rejected submissions are scoped by customer name. In production this would be customerId.
    var rejected = await db.RejectedOrders
        .Where(r => r.CustomerName == customer.Name)
        .OrderByDescending(r => r.CreatedAt)
        .ThenBy(r => r.SourceOrderId)
        .ToListAsync();

    return Results.Ok(rejected.Select(r => new
    {
        r.Id,
        r.SourceOrderId,
        r.CustomerName,
        reasons = DeserializeReasons(r.ReasonsJson),
        r.CreatedAt
    }));
});

app.MapGet("/api/me/events", async (HttpRequest request, AppDbContext db) =>
{
    var customer = await GetCustomerFromHeaderAsync(request, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    var events = await db.EventLogs
        .Where(e => e.CustomerId == customer.Id)
        .OrderByDescending(e => e.CreatedAt)
        .Take(50)
        .ToListAsync();

    return Results.Ok(events.Select(e => new
    {
        e.Id,
        e.Type,
        e.OrderId,
        e.CustomerId,
        e.Message,
        e.CreatedAt
    }));
});

app.MapGet("/api/me/recommendations", async (HttpRequest request, string? query, int? limit, AppDbContext db) =>
{
    var customer = await GetCustomerFromHeaderAsync(request, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    var products = await db.Products.OrderBy(p => p.Name).ToListAsync();

    var popularity = await db.OrderItems
        .GroupBy(i => i.ProductSku)
        .Select(g => new { Sku = g.Key, Count = g.Sum(i => i.Quantity) })
        .ToDictionaryAsync(x => x.Sku, x => x.Count);

    var averageUnitPrices = await db.OrderItems
        .Where(i => i.Quantity > 0)
        .GroupBy(i => i.ProductSku)
        .Select(g => new { Sku = g.Key, UnitPrice = g.Average(i => (double)i.ItemTotal / i.Quantity) })
        .ToDictionaryAsync(x => x.Sku, x => Math.Round((decimal)x.UnitPrice, 2));

    var customerHistory = await db.Orders
        .Where(o => o.CustomerId == customer.Id)
        .SelectMany(o => o.Items)
        .GroupBy(i => i.ProductSku)
        .Select(g => new { Sku = g.Key, Count = g.Sum(i => i.Quantity) })
        .ToDictionaryAsync(x => x.Sku, x => x.Count);

    var recommendations = BuildRecommendations(
        products,
        query ?? string.Empty,
        customerHistory,
        popularity,
        averageUnitPrices,
        Math.Clamp(limit ?? 6, 1, 10));

    return Results.Ok(recommendations);
});

app.MapGet("/api/customers/{customerId:int}/orders", async (int customerId, AppDbContext db) =>
{
    var orders = await db.Orders
        .Where(o => o.CustomerId == customerId)
        .Include(o => o.Items)
        .Include(o => o.StockReservations)
        .OrderByDescending(o => o.DeliveryDate)
        .Select(o => new
        {
            o.Id,
            o.CustomerName,
            deliveryDate = o.DeliveryDate.ToString("dd/MM/yyyy"),
            o.Total,
            items = o.Items.Select(i => new { i.ProductSku, i.ProductName, i.Quantity, i.ItemTotal }),
            stockReservations = o.StockReservations.Select(r => new { r.ProductSku, r.StockBatchId, r.Quantity })
        })
        .ToListAsync();

    return Results.Ok(orders);
});

app.MapGet("/api/events", async (AppDbContext db) =>
{
    var events = await db.EventLogs
        .OrderByDescending(e => e.CreatedAt)
        .Take(100)
        .ToListAsync();

    return Results.Ok(events.Select(e => new
    {
        e.Id,
        e.Type,
        e.OrderId,
        e.CustomerId,
        e.Message,
        e.CreatedAt
    }));
});


app.MapPost("/api/me/orders", async (
    HttpRequest httpRequest,
    CreateOrderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock,
    CustomerService customers,
    EventLogService events) =>
{
    var customer = await GetCustomerFromHeaderAsync(httpRequest, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    // Customer-scoped endpoint: server-side customer identity wins over client-provided name/address.
    var scopedRequest = new CreateOrderRequest
    {
        CustomerName = customer.Name,
        DeliveryAddress = new AddressDto
        {
            AddressLine1 = customer.AddressLine1,
            Postcode = customer.Postcode,
            City = customer.City
        },
        DeliveryDate = request.DeliveryDate,
        Items = request.Items
    };

    return await CreateOrderAsync(scopedRequest, db, validation, stock, customers, events, "CUSTOMER_ORDER_CREATED");
});

app.MapPost("/api/me/orders/{orderId:int}/reorder", async (
    HttpRequest httpRequest,
    int orderId,
    ReorderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock,
    CustomerService customers,
    EventLogService events) =>
{
    var customer = await GetCustomerFromHeaderAsync(httpRequest, db);
    if (customer is null)
    {
        return CustomerRequired();
    }

    var source = await db.Orders
        .Where(o => o.CustomerId == customer.Id)
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == orderId);

    if (source is null)
    {
        return Results.NotFound(new { reasons = new[] { $"Order {orderId} was not found for this customer." } });
    }

    var deliveryDate = string.IsNullOrWhiteSpace(request.DeliveryDate)
        ? source.DeliveryDate.AddDays(7).ToString("dd/MM/yyyy")
        : request.DeliveryDate;

    var reorder = new CreateOrderRequest
    {
        CustomerName = customer.Name,
        DeliveryAddress = new AddressDto
        {
            AddressLine1 = customer.AddressLine1,
            City = customer.City,
            Postcode = customer.Postcode
        },
        DeliveryDate = deliveryDate,
        Items = source.Items.Select(i => new CreateOrderItemRequest
        {
            ProductSku = i.ProductSku,
            Quantity = i.Quantity,
            ItemTotal = i.ItemTotal
        }).ToList()
    };

    return await CreateOrderAsync(reorder, db, validation, stock, customers, events, "CUSTOMER_REORDER_CREATED", orderId);
});

app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock,
    CustomerService customers,
    EventLogService events) =>
{
    return await CreateOrderAsync(request, db, validation, stock, customers, events, "ORDER_CREATED");
});

app.MapPost("/api/orders/{orderId:int}/reorder", async (
    int orderId,
    ReorderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock,
    CustomerService customers,
    EventLogService events) =>
{
    var source = await db.Orders
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == orderId);

    if (source is null)
    {
        return Results.NotFound(new { reasons = new[] { $"Order {orderId} was not found." } });
    }

    var deliveryDate = string.IsNullOrWhiteSpace(request.DeliveryDate)
        ? source.DeliveryDate.AddDays(7).ToString("dd/MM/yyyy")
        : request.DeliveryDate;

    var reorder = new CreateOrderRequest
    {
        CustomerName = source.CustomerName,
        DeliveryAddress = new AddressDto
        {
            AddressLine1 = source.AddressLine1,
            City = source.City,
            Postcode = source.Postcode
        },
        DeliveryDate = deliveryDate,
        Items = source.Items.Select(i => new CreateOrderItemRequest
        {
            ProductSku = i.ProductSku,
            Quantity = i.Quantity,
            ItemTotal = i.ItemTotal
        }).ToList()
    };

    return await CreateOrderAsync(reorder, db, validation, stock, customers, events, "REORDER_CREATED", orderId);
});

app.Run();


static async Task<Customer?> GetCustomerFromHeaderAsync(HttpRequest request, AppDbContext db)
{
    if (!request.Headers.TryGetValue("X-Customer-Id", out var headerValue))
    {
        return null;
    }

    var rawId = headerValue.FirstOrDefault();
    if (!int.TryParse(rawId, out var customerId))
    {
        return null;
    }

    return await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
}

static IResult CustomerRequired()
{
    return Results.Json(
        new { reasons = new[] { "Select a customer before using the customer portal." } },
        statusCode: StatusCodes.Status401Unauthorized);
}

static async Task<IResult> CreateOrderAsync(
    CreateOrderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock,
    CustomerService customers,
    EventLogService events,
    string eventType,
    int? sourceOrderId = null)
{
    var productsBySku = await db.Products.ToDictionaryAsync(p => p.Sku, p => p);
    var newOrderId = await GetNextOrderIdAsync(db);
    var validationResult = validation.ValidateNewOrder(request, newOrderId, productsBySku);

    if (!validationResult.IsValid || validationResult.Draft is null)
    {
        db.RejectedOrders.Add(new RejectedOrder
        {
            SourceOrderId = sourceOrderId,
            CustomerName = request.CustomerName?.Trim() ?? string.Empty,
            ReasonsJson = JsonSerializer.Serialize(validationResult.Reasons),
            RawJson = JsonSerializer.Serialize(request),
            CreatedAt = DateTime.UtcNow
        });
        events.Add("ORDER_REJECTED", $"Rejected new order: {string.Join("; ", validationResult.Reasons)}", sourceOrderId);
        await db.SaveChangesAsync();
        return Results.BadRequest(new { reasons = validationResult.Reasons });
    }

    var draft = validationResult.Draft;
    var customer = await customers.FindOrCreateAsync(draft.CustomerName, draft.DeliveryAddress);
    var order = SeedDataService.CreateOrderFromDraft(draft, customer.Id);

    var stockResult = await stock.ReserveAsync(order.Id, order.DeliveryDate, order.Items);
    if (!stockResult.Success)
    {
        db.RejectedOrders.Add(new RejectedOrder
        {
            SourceOrderId = sourceOrderId,
            CustomerName = request.CustomerName?.Trim() ?? string.Empty,
            ReasonsJson = JsonSerializer.Serialize(stockResult.Reasons),
            RawJson = JsonSerializer.Serialize(request),
            CreatedAt = DateTime.UtcNow
        });
        events.Add("ORDER_REJECTED", $"Rejected new order: {string.Join("; ", stockResult.Reasons)}", sourceOrderId, customer.Id);
        await db.SaveChangesAsync();
        return Results.BadRequest(new { reasons = stockResult.Reasons });
    }

    order.StockReservations = stockResult.Reservations;
    db.Orders.Add(order);

    events.Add(eventType, sourceOrderId is null
        ? $"Created order {order.Id} for {order.CustomerName}."
        : $"Created reorder {order.Id} from order {sourceOrderId} for {order.CustomerName}.",
        order.Id,
        customer.Id,
        new { sourceOrderId });

    events.Add("STOCK_RESERVED", $"Reserved stock for order {order.Id} using earliest-expiring valid batches.", order.Id, customer.Id,
        stockResult.Reservations.Select(r => new { r.ProductSku, r.StockBatchId, r.Quantity }));

    await db.SaveChangesAsync();

    return Results.Created($"/api/orders/{order.Id}", new
    {
        order.Id,
        order.CustomerId,
        order.CustomerName,
        deliveryAddress = new { order.AddressLine1, order.Postcode, order.City },
        deliveryDate = order.DeliveryDate.ToString("dd/MM/yyyy"),
        order.Total,
        items = order.Items.Select(i => new { i.ProductSku, i.ProductName, i.Quantity, i.ItemTotal }),
        stockReservations = order.StockReservations.Select(r => new { r.ProductSku, r.StockBatchId, r.Quantity })
    });
}

static async Task<IResult> PreviewOrderAsync(
    CreateOrderRequest request,
    AppDbContext db,
    OrderValidationService validation,
    StockAllocationService stock)
{
    var productsBySku = await db.Products.ToDictionaryAsync(p => p.Sku, p => p);
    var previewOrderId = await GetNextOrderIdAsync(db);

    var validationResult = validation.ValidateNewOrder(request, previewOrderId, productsBySku);

    if (!validationResult.IsValid || validationResult.Draft is null)
    {
        return Results.Ok(new OrderPreviewResponse
        {
            CanSubmit = false,
            Reasons = validationResult.Reasons,
            Warnings = new List<string>
            {
                "This preview did not reserve stock. Fix the validation issues and check again before submitting."
            }
        });
    }

    var draft = validationResult.Draft;
    var stockPreview = await stock.PreviewAsync(draft.DeliveryDate, draft.Items);

    return Results.Ok(new OrderPreviewResponse
    {
        CanSubmit = stockPreview.Success,
        Reasons = stockPreview.Reasons,
        Warnings = stockPreview.Success
            ? new List<string>
            {
                "Stock is currently available for this delivery date.",
                "Preview does not reserve stock. Submit will revalidate and reserve stock server-side."
            }
            : new List<string>
            {
                "No stock was reserved because this is only a preview.",
                "Change the basket or delivery date, then check availability again."
            },
        DeliveryDate = draft.DeliveryDate.ToString("dd/MM/yyyy"),
        Total = draft.Total,
        Items = draft.Items.Select(i => new OrderPreviewItemDto
        {
            ProductSku = i.ProductSku,
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            ItemTotal = i.ItemTotal
        }).ToList(),
        PlannedReservations = stockPreview.PlannedReservations
    });
}

static async Task<int> GetNextOrderIdAsync(AppDbContext db)
{
    var maxOrderId = await db.Orders.Select(o => (int?)o.Id).MaxAsync() ?? 1000;
    return maxOrderId + 1;
}

static List<string> DeserializeReasons(string json)
{
    try
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }
    catch
    {
        return new List<string> { json };
    }
}

static List<ProductRecommendation> BuildRecommendations(
    List<Product> products,
    string query,
    Dictionary<string, int> customerHistory,
    Dictionary<string, int> popularity,
    Dictionary<string, decimal> averageUnitPrices,
    int limit)
{
    var queryLower = query.Trim().ToLowerInvariant();
    var tokens = queryLower
        .Split(new[] { ' ', ',', '.', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim())
        .Where(t => t.Length > 1)
        .ToHashSet();

    var recommendations = new List<ProductRecommendation>();

    foreach (var product in products)
    {
        var score = 0.0;
        var reasons = new List<string>();
        var nameLower = product.Name.ToLowerInvariant();
        var nameTokens = nameLower.Split(new[] { ' ', ',', '.', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Count > 0 && nameTokens.Any(tokens.Contains))
        {
            score += 2;
            reasons.Add("matches the product name");
        }

        if (RecommendationData.ProductAliases.TryGetValue(product.Sku, out var aliases))
        {
            var matchedAliases = aliases.Where(alias => queryLower.Contains(alias)).Distinct().ToList();
            if (matchedAliases.Count > 0)
            {
                score += 3 + matchedAliases.Count;
                reasons.Add($"matches {string.Join(", ", matchedAliases.Take(3))} search terms");
            }
        }

        foreach (var scenario in RecommendationData.ScenarioSkus)
        {
            if (queryLower.Contains(scenario.Key) && scenario.Value.Contains(product.Sku))
            {
                score += 5;
                reasons.Add($"fits a {scenario.Key} scenario");
            }
        }

        if (customerHistory.TryGetValue(product.Sku, out var customerQuantity))
        {
            score += 4 + Math.Min(customerQuantity, 10) * 0.2;
            reasons.Add("previously ordered by this customer");
        }

        if (popularity.TryGetValue(product.Sku, out var totalQuantity))
        {
            score += Math.Min(totalQuantity, 20) * 0.05;
            if (reasons.Count == 0 && totalQuantity > 0)
            {
                reasons.Add("popular in existing valid orders");
            }
        }

        if (score > 0)
        {
            recommendations.Add(new ProductRecommendation
            {
                Sku = product.Sku,
                Name = product.Name,
                Score = Math.Round(score, 2),
                SuggestedUnitPrice = averageUnitPrices.TryGetValue(product.Sku, out var unitPrice) ? unitPrice : null,
                Reason = string.Join("; ", reasons.Distinct())
            });
        }
    }

    if (recommendations.Count == 0)
    {
        recommendations = products
            .OrderByDescending(p => popularity.GetValueOrDefault(p.Sku))
            .ThenBy(p => p.Name)
            .Take(limit)
            .Select(p => new ProductRecommendation
            {
                Sku = p.Sku,
                Name = p.Name,
                Score = 0.5,
                SuggestedUnitPrice = averageUnitPrices.TryGetValue(p.Sku, out var unitPrice) ? unitPrice : null,
                Reason = popularity.ContainsKey(p.Sku)
                    ? "popular in existing valid orders"
                    : "valid catalogue product"
            })
            .ToList();
    }

    return recommendations
        .OrderByDescending(r => r.Score)
        .ThenBy(r => r.Name)
        .Take(limit)
        .ToList();
}

public static class RecommendationData
{
    public static readonly Dictionary<string, string[]> ProductAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YR9U90T57MR0"] = new[] { "milk", "dairy", "breakfast", "cafe", "coffee", "brunch" },
        ["AB12CD34EF56"] = new[] { "egg", "eggs", "breakfast", "brunch", "protein", "cafe" },
        ["GH78JK90LM12"] = new[] { "bread", "sourdough", "toast", "bakery", "breakfast", "brunch", "sandwich" },
        ["UV90WX12YZ34"] = new[] { "rice", "basmati", "lunch", "dinner", "side", "catering" },
        ["M6L5K4J3H2G1"] = new[] { "ham", "meat", "sandwich", "lunch", "deli" },
        ["U7I8O9P1A2S3"] = new[] { "tomato", "tomatoes", "salad", "vegetarian", "brunch", "lunch", "fresh" },
        ["E7R8T9Y1U2I3"] = new[] { "fish", "haddock", "seafood", "dinner", "protein" },
        ["X7C8V9B1N2M3"] = new[] { "juice", "apple", "drink", "drinks", "breakfast", "brunch" },
        ["K2L3M4N5O6P7"] = new[] { "bread", "wholemeal", "bakery", "sandwich", "lunch", "toast" },
        ["R8S9T1U2V3W4"] = new[] { "buns", "bun", "cinnamon", "bakery", "dessert", "breakfast", "sweet" },
        ["H5I6J7K8L9M1"] = new[] { "cream", "clotted", "dessert", "bakery", "dairy", "scone" },
        ["Q1W2E3R4T5Y6"] = new[] { "water", "still", "drink", "drinks", "lunch", "service" }
    };

    public static readonly Dictionary<string, string[]> ScenarioSkus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["breakfast"] = new[] { "AB12CD34EF56", "YR9U90T57MR0", "GH78JK90LM12", "X7C8V9B1N2M3", "R8S9T1U2V3W4" },
        ["brunch"] = new[] { "AB12CD34EF56", "YR9U90T57MR0", "GH78JK90LM12", "U7I8O9P1A2S3", "X7C8V9B1N2M3" },
        ["cafe"] = new[] { "YR9U90T57MR0", "AB12CD34EF56", "GH78JK90LM12", "X7C8V9B1N2M3", "R8S9T1U2V3W4" },
        ["bakery"] = new[] { "GH78JK90LM12", "K2L3M4N5O6P7", "R8S9T1U2V3W4", "H5I6J7K8L9M1" },
        ["lunch"] = new[] { "K2L3M4N5O6P7", "M6L5K4J3H2G1", "U7I8O9P1A2S3", "Q1W2E3R4T5Y6", "UV90WX12YZ34" },
        ["dinner"] = new[] { "E7R8T9Y1U2I3", "UV90WX12YZ34", "U7I8O9P1A2S3", "Q1W2E3R4T5Y6" },
        ["seafood"] = new[] { "E7R8T9Y1U2I3", "Q1W2E3R4T5Y6" },
        ["drinks"] = new[] { "Q1W2E3R4T5Y6", "X7C8V9B1N2M3", "YR9U90T57MR0" }
    };
}

public class ReorderRequest
{
    public string? DeliveryDate { get; set; }
}

public class ProductRecommendation
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Score { get; set; }
    public decimal? SuggestedUnitPrice { get; set; }
    public string Reason { get; set; } = string.Empty;
}

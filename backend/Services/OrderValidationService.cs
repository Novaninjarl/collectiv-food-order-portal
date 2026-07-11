using System.Globalization;
using CollectivOrder.Api.DTOs;
using CollectivOrder.Api.Models;

namespace CollectivOrder.Api.Services;

public class OrderValidationService
{
    public OrderValidationResult Validate(RawOrderDto order, IReadOnlyDictionary<string, Product> productsBySku, HashSet<int> existingOrderIds)
    {
        var result = new OrderValidationResult();

        if (order.Id <= 0)
        {
            result.Reasons.Add("Order ID is required and must be positive.");
        }

        if (existingOrderIds.Contains(order.Id))
        {
            result.Reasons.Add($"Duplicate order ID {order.Id}.");
        }

        ValidateSharedFields(
            order.CustomerName,
            order.DeliveryAddress,
            order.DeliveryDate,
            order.Items,
            order.Total,
            productsBySku,
            result,
            out var deliveryDate,
            out var items,
            out var total
        );

        if (result.Reasons.Count == 0)
        {
            result.Draft = new ValidatedOrderDraft
            {
                Id = order.Id,
                CustomerName = order.CustomerName!.Trim(),
                DeliveryAddress = NormaliseAddress(order.DeliveryAddress!),
                DeliveryDate = deliveryDate,
                Total = total,
                Items = items
            };
        }

        return result;
    }

    public OrderValidationResult ValidateNewOrder(CreateOrderRequest request, int newOrderId, IReadOnlyDictionary<string, Product> productsBySku)
    {
        var result = new OrderValidationResult();
        var rawItems = request.Items.Select(i => new RawOrderItemDto
        {
            ProductSku = i.ProductSku,
            Quantity = i.Quantity,
            ItemTotal = i.ItemTotal
        }).ToList();
        var requestedTotal = rawItems.Sum(i => i.ItemTotal);

        ValidateSharedFields(
            request.CustomerName,
            request.DeliveryAddress,
            request.DeliveryDate,
            rawItems,
            requestedTotal,
            productsBySku,
            result,
            out var deliveryDate,
            out var items,
            out var total
        );

        if (result.Reasons.Count == 0)
        {
            result.Draft = new ValidatedOrderDraft
            {
                Id = newOrderId,
                CustomerName = request.CustomerName!.Trim(),
                DeliveryAddress = NormaliseAddress(request.DeliveryAddress!),
                DeliveryDate = deliveryDate,
                Total = total,
                Items = items
            };
        }

        return result;
    }

    private static void ValidateSharedFields(
        string? customerName,
        AddressDto? address,
        string? deliveryDateText,
        List<RawOrderItemDto>? rawItems,
        decimal requestedTotal,
        IReadOnlyDictionary<string, Product> productsBySku,
        OrderValidationResult result,
        out DateTime deliveryDate,
        out List<OrderItem> items,
        out decimal total)
    {
        deliveryDate = default;
        items = new List<OrderItem>();
        total = decimal.Round(requestedTotal, 2);

        if (string.IsNullOrWhiteSpace(customerName))
        {
            result.Reasons.Add("Customer name is required.");
        }

        if (address is null)
        {
            result.Reasons.Add("Delivery address is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(address.AddressLine1)) result.Reasons.Add("Address line 1 is required.");
            if (string.IsNullOrWhiteSpace(address.Postcode)) result.Reasons.Add("Postcode is required.");
            if (string.IsNullOrWhiteSpace(address.City)) result.Reasons.Add("City is required.");
        }

        if (!DateTime.TryParseExact(deliveryDateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out deliveryDate))
        {
            result.Reasons.Add("Delivery date must be a real date in DD/MM/YYYY format.");
        }

        if (rawItems is null || rawItems.Count == 0)
        {
            result.Reasons.Add("Order must contain at least one item.");
            return;
        }

        for (var index = 0; index < rawItems.Count; index++)
        {
            var rawItem = rawItems[index];
            var label = $"Item {index + 1}";
            var normalisedSku = NormaliseSku(rawItem.ProductSku);

            if (string.IsNullOrWhiteSpace(normalisedSku))
            {
                result.Reasons.Add($"{label}: product SKU is required.");
                continue;
            }

            if (!productsBySku.TryGetValue(normalisedSku, out var product))
            {
                result.Reasons.Add($"{label}: unknown product SKU '{rawItem.ProductSku}'.");
                continue;
            }

            if (rawItem.Quantity <= 0)
            {
                result.Reasons.Add($"{label}: quantity must be greater than zero.");
            }

            if (rawItem.ItemTotal <= 0)
            {
                result.Reasons.Add($"{label}: item total must be greater than zero.");
            }

            if (!HasMaxTwoDecimalPlaces(rawItem.ItemTotal))
            {
                result.Reasons.Add($"{label}: item total must have no more than 2 decimal places.");
            }

            items.Add(new OrderItem
            {
                ProductSku = product.Sku,
                ProductName = product.Name,
                Quantity = rawItem.Quantity,
                ItemTotal = decimal.Round(rawItem.ItemTotal, 2)
            });
        }

        var calculatedTotal = items.Sum(i => i.ItemTotal);

        if (requestedTotal < 0)
        {
            result.Reasons.Add("Order total cannot be negative.");
        }

        if (!HasMaxTwoDecimalPlaces(requestedTotal))
        {
            result.Reasons.Add("Order total must have no more than 2 decimal places.");
        }

        if (decimal.Round(calculatedTotal, 2) != decimal.Round(requestedTotal, 2))
        {
            result.Reasons.Add($"Order total {requestedTotal:0.00} does not match item totals {calculatedTotal:0.00}.");
        }
    }

    private static AddressDto NormaliseAddress(AddressDto address) => new()
    {
        AddressLine1 = address.AddressLine1?.Trim(),
        City = address.City?.Trim(),
        Postcode = address.Postcode?.Trim().ToUpperInvariant()
    };

    public static string NormaliseSku(string? sku) => sku?.Trim().ToUpperInvariant() ?? string.Empty;

    private static bool HasMaxTwoDecimalPlaces(decimal value)
    {
        return decimal.Round(value, 2) == value;
    }
}

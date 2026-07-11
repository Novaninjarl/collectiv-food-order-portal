using CollectivOrder.Api.Data;
using CollectivOrder.Api.DTOs;
using CollectivOrder.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectivOrder.Api.Services;

public class StockAllocationService
{
    private readonly AppDbContext _db;

    public StockAllocationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StockAllocationPreviewResult> PreviewAsync(DateTime deliveryDate, List<OrderItem> items)
    {
        var skus = items.Select(i => i.ProductSku).Distinct().ToList();

        // AsNoTracking is important here: preview must not change stock quantities.
        var candidateBatches = await _db.StockBatches
            .AsNoTracking()
            .Where(b => skus.Contains(b.ProductSku) && b.AvailableQuantity > 0 && b.ExpiryDate.Date >= deliveryDate.Date)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchId)
            .ToListAsync();

        var tempAvailable = candidateBatches.ToDictionary(b => b.BatchId, b => b.AvailableQuantity);
        var plannedReservations = new List<PlannedStockReservationDto>();
        var reasons = new List<string>();

        foreach (var item in items)
        {
            var remaining = item.Quantity;

            var productBatches = candidateBatches
                .Where(b => b.ProductSku == item.ProductSku)
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.BatchId)
                .ToList();

            foreach (var batch in productBatches)
            {
                if (remaining == 0) break;

                var available = tempAvailable[batch.BatchId];
                if (available <= 0) continue;

                var allocated = Math.Min(available, remaining);
                tempAvailable[batch.BatchId] -= allocated;
                remaining -= allocated;

                plannedReservations.Add(new PlannedStockReservationDto
                {
                    ProductSku = item.ProductSku,
                    ProductName = item.ProductName,
                    StockBatchId = batch.BatchId,
                    Quantity = allocated,
                    ExpiryDate = batch.ExpiryDate.ToString("dd/MM/yyyy")
                });
            }

            if (remaining > 0)
            {
                reasons.Add($"Insufficient non-expired stock for {item.ProductName}. Needed {item.Quantity}, short by {remaining} for delivery date {deliveryDate:dd/MM/yyyy}.");
            }
        }

        return new StockAllocationPreviewResult(
            reasons.Count == 0,
            reasons,
            reasons.Count == 0 ? plannedReservations : new List<PlannedStockReservationDto>());
    }

    public async Task<StockAllocationResult> ReserveAsync(int orderId, DateTime deliveryDate, List<OrderItem> items)
    {
        var skus = items.Select(i => i.ProductSku).Distinct().ToList();

        var candidateBatches = await _db.StockBatches
            .Where(b => skus.Contains(b.ProductSku) && b.AvailableQuantity > 0 && b.ExpiryDate.Date >= deliveryDate.Date)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchId)
            .ToListAsync();

        var tempAvailable = candidateBatches.ToDictionary(b => b.BatchId, b => b.AvailableQuantity);
        var plannedReservations = new List<StockReservation>();
        var reasons = new List<string>();

        foreach (var item in items)
        {
            var remaining = item.Quantity;

            var productBatches = candidateBatches
                .Where(b => b.ProductSku == item.ProductSku)
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.BatchId)
                .ToList();

            foreach (var batch in productBatches)
            {
                if (remaining == 0) break;

                var available = tempAvailable[batch.BatchId];
                if (available <= 0) continue;

                var allocated = Math.Min(available, remaining);
                tempAvailable[batch.BatchId] -= allocated;
                remaining -= allocated;

                plannedReservations.Add(new StockReservation
                {
                    OrderId = orderId,
                    ProductSku = item.ProductSku,
                    StockBatchId = batch.BatchId,
                    Quantity = allocated,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (remaining > 0)
            {
                reasons.Add($"Insufficient non-expired stock for {item.ProductName}. Needed {item.Quantity}, short by {remaining} for delivery date {deliveryDate:dd/MM/yyyy}.");
            }
        }

        if (reasons.Count > 0)
        {
            return new StockAllocationResult(false, reasons, new List<StockReservation>());
        }

        foreach (var batch in candidateBatches)
        {
            batch.AvailableQuantity = tempAvailable[batch.BatchId];
        }

        return new StockAllocationResult(true, reasons, plannedReservations);
    }

    public async Task ReleaseReservationsForOrderAsync(int orderId)
    {
        var reservations = await _db.StockReservations.Where(r => r.OrderId == orderId).ToListAsync();

        foreach (var reservation in reservations)
        {
            var batch = await _db.StockBatches.FindAsync(reservation.StockBatchId);
            if (batch is not null)
            {
                batch.AvailableQuantity += reservation.Quantity;
            }
        }

        _db.StockReservations.RemoveRange(reservations);
    }
}

public record StockAllocationResult(bool Success, List<string> Reasons, List<StockReservation> Reservations);

public record StockAllocationPreviewResult(
    bool Success,
    List<string> Reasons,
    List<PlannedStockReservationDto> PlannedReservations);
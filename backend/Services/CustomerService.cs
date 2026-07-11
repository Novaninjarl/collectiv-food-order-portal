using CollectivOrder.Api.Data;
using CollectivOrder.Api.DTOs;
using CollectivOrder.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectivOrder.Api.Services;

public class CustomerService
{
    private readonly AppDbContext _db;

    public CustomerService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Customer> FindOrCreateAsync(string name, AddressDto address)
    {
        var normalisedName = name.Trim();
        var normalisedPostcode = address.Postcode?.Trim().ToUpperInvariant() ?? string.Empty;

        var existing = await _db.Customers.FirstOrDefaultAsync(c =>
            c.Name == normalisedName && c.Postcode == normalisedPostcode);

        if (existing is not null)
        {
            return existing;
        }

        var customer = new Customer
        {
            Name = normalisedName,
            AddressLine1 = address.AddressLine1?.Trim() ?? string.Empty,
            City = address.City?.Trim() ?? string.Empty,
            Postcode = normalisedPostcode,
            CreatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }
}

using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public sealed class ServiceCatalogService : IServiceCatalogService
{
    private readonly IAppDbContext _db;
    public ServiceCatalogService(IAppDbContext db) => _db = db;

    public async Task<List<ServiceDto>> ListAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var services = await _db.ShopServices.AsNoTracking()
            .Where(s => s.ShopId == shopId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new ServiceDto
            {
                Id = s.Id, Name = s.Name, DefaultPrice = s.DefaultPrice,
                IsActive = s.IsActive, SortOrder = s.SortOrder
            }).ToListAsync(cancellationToken);
        if (services.Count > 0)
        {
            // Upgraded shops may already have a catalogue but still carry the old flag.
            var existing = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
            if (existing != null && !existing.CatalogueInitialized)
            {
                existing.CatalogueInitialized = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return services;
        }
        var shop = await _db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        if (shop == null || shop.CatalogueInitialized) return services;

        // Shops created before this feature had no catalogue. Backfill once.
        var initializing = await _db.Shops.FirstAsync(s => s.Id == shopId, cancellationToken);
        initializing.CatalogueInitialized = true;
        _db.ShopServices.AddRange(
            new ShopService { ShopId = shopId, Name = "Wash & Fold", NormalizedName = "wash & fold", DefaultPrice = 120, SortOrder = 0 },
            new ShopService { ShopId = shopId, Name = "Dry Clean", NormalizedName = "dry clean", DefaultPrice = 250, SortOrder = 1 },
            new ShopService { ShopId = shopId, Name = "Ironing", NormalizedName = "ironing", DefaultPrice = 80, SortOrder = 2 });
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* another request seeded first */ }
        return await _db.ShopServices.AsNoTracking().Where(s => s.ShopId == shopId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new ServiceDto
            {
                Id = s.Id, Name = s.Name, DefaultPrice = s.DefaultPrice,
                IsActive = s.IsActive, SortOrder = s.SortOrder
            }).ToListAsync(cancellationToken);
    }

    public async Task<ServiceDto> CreateAsync(Guid shopId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new AppException("A service name is required.");
        if (await _db.ShopServices.AnyAsync(s => s.ShopId == shopId && s.Name.ToLower() == name.ToLower(),
                cancellationToken))
            throw new AppException("A service with this name already exists.");
        var service = new ShopService
        {
            ShopId = shopId, Name = name, NormalizedName = name.ToLowerInvariant(),
            DefaultPrice = request.DefaultPrice, SortOrder = request.SortOrder
        };
        _db.ShopServices.Add(service);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(service);
    }

    public async Task<ServiceDto> UpdateAsync(Guid shopId, Guid id, UpdateServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var service = await _db.ShopServices.FirstOrDefaultAsync(s => s.Id == id && s.ShopId == shopId,
            cancellationToken) ?? throw new AppException("Service not found.");
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new AppException("A service name is required.");
        if (await _db.ShopServices.AnyAsync(s => s.ShopId == shopId && s.Id != id &&
                s.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new AppException("A service with this name already exists.");
        service.Name = name;
        service.NormalizedName = name.ToLowerInvariant();
        service.DefaultPrice = request.DefaultPrice;
        service.IsActive = request.IsActive;
        service.SortOrder = request.SortOrder;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(service);
    }

    public async Task DeleteAsync(Guid shopId, Guid id, CancellationToken cancellationToken = default)
    {
        var service = await _db.ShopServices.FirstOrDefaultAsync(s => s.Id == id && s.ShopId == shopId,
            cancellationToken) ?? throw new AppException("Service not found.");
        _db.ShopServices.Remove(service);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ServiceDto Map(ShopService s) => new()
    {
        Id = s.Id, Name = s.Name, DefaultPrice = s.DefaultPrice,
        IsActive = s.IsActive, SortOrder = s.SortOrder
    };
}

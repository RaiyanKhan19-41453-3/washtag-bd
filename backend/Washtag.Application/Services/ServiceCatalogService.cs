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

    public async Task<List<ServiceDto>> ListAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await _db.ShopServices.AsNoTracking()
            .Where(s => s.ShopId == shopId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new ServiceDto
            {
                Id = s.Id, Name = s.Name, DefaultPrice = s.DefaultPrice,
                IsActive = s.IsActive, SortOrder = s.SortOrder
            }).ToListAsync(cancellationToken);

    public async Task<ServiceDto> CreateAsync(Guid shopId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await _db.ShopServices.AnyAsync(s => s.ShopId == shopId && s.Name.ToLower() == name.ToLower(),
                cancellationToken))
            throw new AppException("A service with this name already exists.");
        var service = new ShopService
        {
            ShopId = shopId, Name = name, DefaultPrice = request.DefaultPrice,
            SortOrder = request.SortOrder
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
        service.Name = request.Name.Trim();
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

using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class StaffService : IStaffService
{
    private readonly IAppDbContext _db;

    public StaffService(IAppDbContext db) => _db = db;

    public async Task<List<StaffDto>> ListAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.ShopId == shopId)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new StaffDto
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Phone = u.Phone,
                Role = u.Role,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            }).ToListAsync(cancellationToken);
    }

    public async Task<StaffDto> CreateAsync(Guid shopId, CreateStaffRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Role != UserRole.Staff)
            throw new AppException("Only staff accounts can be created from the team page.");

        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await _db.Users.FirstOrDefaultAsync(
            u => u.ShopId == shopId && u.Email.ToLower() == email, cancellationToken);
        if (existing != null)
        {
            if (existing.IsActive || existing.Role != UserRole.Staff)
                throw new AppException("That email address is already in use.");
            existing.Name = request.Name.Trim();
            existing.Phone = request.Phone?.Trim() ?? string.Empty;
            existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            existing.IsActive = true;
            await _db.SaveChangesAsync(cancellationToken);
            return Map(existing);
        }

        var user = new AppUser
        {
            ShopId = shopId,
            Name = request.Name.Trim(),
            Email = email,
            Phone = request.Phone?.Trim() ?? string.Empty,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Staff,
            IsActive = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<StaffDto> UpdateAsync(Guid shopId, Guid userId, UpdateStaffRequest request, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(shopId, userId, cancellationToken);
        if (user.Role == UserRole.Owner)
            throw new AppException("The shop owner cannot be edited from the team page.");

        user.Name = request.Name.Trim();
        user.Phone = request.Phone?.Trim() ?? string.Empty;
        if (user.IsActive && !request.IsActive)
        {
            var tokens = await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var token in tokens) token.RevokedAt = DateTime.UtcNow;
        }
        user.IsActive = request.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task DeactivateAsync(Guid shopId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(shopId, userId, cancellationToken);
        if (user.Role == UserRole.Owner)
            throw new AppException("The shop owner cannot be deactivated.");
        user.IsActive = false;
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens) token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AppUser> FindAsync(Guid shopId, Guid userId, CancellationToken ct) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.ShopId == shopId, ct)
        ?? throw new AppException("Team member not found.");

    private static StaffDto Map(AppUser user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Phone = user.Phone,
        Role = user.Role,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt
    };
}

using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IJwtTokenGenerator _tokens;

    public AuthService(IAppDbContext db, IJwtTokenGenerator tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            throw new AppException("An account with this email already exists.");

        var shop = new Shop
        {
            Name = request.ShopName.Trim(),
            OwnerName = request.OwnerName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            Address = request.Address.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? "Dhaka" : request.City.Trim(),
            Plan = string.IsNullOrWhiteSpace(request.Plan) ? "Starter" : request.Plan.Trim(),
            SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1)
        };

        var user = new AppUser
        {
            Shop = shop,
            Name = request.OwnerName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Owner,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            Token = _tokens.Generate(user, shop),
            User = MapUser(user),
            Shop = MapShop(shop)
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.Shop)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new AppException("Invalid email or password.");
        if (!user.IsActive || !user.Shop.IsActive)
            throw new AppException("This account is inactive. Please contact your shop owner.");

        return new AuthResponse
        {
            Token = _tokens.Generate(user, user.Shop),
            User = MapUser(user),
            Shop = MapShop(user.Shop)
        };
    }

    public async Task<AuthResponse> GetProfileAsync(Guid userId)
    {
        var user = await _db.Users.Include(u => u.Shop).FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new AppException("Account not found.");

        return new AuthResponse
        {
            Token = string.Empty,
            User = MapUser(user),
            Shop = MapShop(user.Shop)
        };
    }

    internal static UserDto MapUser(AppUser user) => new()
    {
        Id = user.Id,
        ShopId = user.ShopId,
        Name = user.Name,
        Email = user.Email,
        Phone = user.Phone,
        Role = user.Role
    };

    internal static ShopDto MapShop(Shop shop) => new()
    {
        Id = shop.Id,
        Name = shop.Name,
        OwnerName = shop.OwnerName,
        Email = shop.Email,
        Phone = shop.Phone,
        Address = shop.Address,
        City = shop.City,
        Plan = shop.Plan,
        LogoUrl = shop.LogoUrl,
        IsActive = shop.IsActive,
        SubscriptionEndsAt = shop.SubscriptionEndsAt,
        CreatedAt = shop.CreatedAt
    };
}

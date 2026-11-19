using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IJwtTokenGenerator _tokens;
    private readonly IAdminSettings _admin;
    private readonly IRefreshTokenStore _refreshTokens;

    public AuthService(IAppDbContext db, IJwtTokenGenerator tokens, IAdminSettings admin,
        IRefreshTokenStore refreshTokens)
    {
        _db = db;
        _tokens = tokens;
        _admin = admin;
        _refreshTokens = refreshTokens;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (_admin.IsAdmin(email))
            throw new AppException("This email address is reserved for platform administration.");
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            throw new AppException("An account with this email already exists.");

        var shop = new Shop
        {
            Name = request.ShopName.Trim(),
            OwnerName = request.OwnerName.Trim(),
            Email = email,
            Phone = request.Phone?.Trim() ?? string.Empty,
            Address = request.Address?.Trim() ?? string.Empty,
            City = string.IsNullOrWhiteSpace(request.City) ? "Dhaka" : request.City.Trim(),
            Plan = Plans.IsValid(request.Plan) ? Plans.All.First(p => p.Equals(request.Plan.Trim(), StringComparison.OrdinalIgnoreCase)) : "Starter",
            SubscriptionStatus = SubscriptionStatus.Trial,
            SubscriptionStartedAt = DateTime.UtcNow,
            SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1)
        };

        var user = new AppUser
        {
            Shop = shop,
            Name = request.OwnerName.Trim(),
            Email = email,
            Phone = request.Phone?.Trim() ?? string.Empty,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Owner,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return await IssueTokensAsync(user, shop);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        try { await _refreshTokens.PurgeExpiredAsync(); } catch { /* best-effort cleanup */ }
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.Shop)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new AppException("Invalid email or password.");
        if (!user.IsActive || !user.Shop.IsActive)
            throw new AppException("This account is inactive. Please contact your shop owner.");

        return await IssueTokensAsync(user, user.Shop);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var userId = await _refreshTokens.RotateAsync(Hash(refreshToken), Hash(rawRefresh),
            DateTime.UtcNow.AddDays(30));
        if (userId == null) throw new AppException("Invalid or expired refresh token.");

        var user = await _db.Users.Include(u => u.Shop).FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user == null || !user.IsActive || !user.Shop.IsActive)
            throw new AppException("Invalid or expired refresh token.");

        return new AuthResponse
        {
            Token = _tokens.Generate(user, user.Shop),
            RefreshToken = rawRefresh,
            User = MapUser(user),
            Shop = MapShop(user.Shop)
        };
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new AppException("Account not found.");
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new AppException("Your current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
        await _refreshTokens.RevokeAllAsync(userId);
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

    private async Task<AuthResponse> IssueTokensAsync(AppUser user, Shop shop)
    {
        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            Token = _tokens.Generate(user, shop),
            RefreshToken = rawRefresh,
            User = MapUser(user),
            Shop = MapShop(shop)
        };
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private UserDto MapUser(AppUser user) => new()
    {
        Id = user.Id,
        ShopId = user.ShopId,
        Name = user.Name,
        Email = user.Email,
        Phone = user.Phone,
        Role = user.Role,
        IsAdmin = _admin.IsAdmin(user.Email)
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

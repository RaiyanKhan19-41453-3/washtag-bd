using System.Security.Cryptography;
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
    private readonly IAdminSettings _admin;

    public AuthService(IAppDbContext db, IJwtTokenGenerator tokens, IAdminSettings admin)
    {
        _db = db;
        _tokens = tokens;
        _admin = admin;
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
            SubscriptionStatus = SubscriptionStatus.Trial,
            SubscriptionStartedAt = DateTime.UtcNow,
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
        return await IssueTokensAsync(user, shop);
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

        return await IssueTokensAsync(user, user.Shop);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        var hash = Hash(refreshToken);
        var stored = await _db.RefreshTokens.Include(t => t.User).ThenInclude(u => u.Shop)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow);

        if (stored == null || !stored.User.IsActive || !stored.User.Shop.IsActive)
            throw new AppException("Invalid or expired refresh token.");

        stored.RevokedAt = DateTime.UtcNow;
        stored.ReplacedBy = "rotated";
        var response = await IssueTokensAsync(stored.User, stored.User.Shop);
        stored.ReplacedBy = response.RefreshToken;
        await _db.SaveChangesAsync();
        return response;
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new AppException("Account not found.");
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new AppException("Your current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var token in activeTokens) token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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

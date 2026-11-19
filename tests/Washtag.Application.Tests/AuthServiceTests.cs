using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Services;
using Washtag.Domain.Entities;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Application.Tests;

public class AuthServiceTests
{
    private static AuthService Create(out TestDbContext db, out FakeRefreshTokenStore refreshStore, params string[] adminEmails)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        db = new TestDbContext(options);
        refreshStore = new FakeRefreshTokenStore();
        return new AuthService(db, new FakeTokens(), new FakeAdminSettings(adminEmails), refreshStore, new FakeCache());
    }

    [Fact]
    public async Task Register_RejectsAdminEmail()
    {
        var service = Create(out _, out _, "admin@test.com");

        await Assert.ThrowsAsync<AppException>(() => service.RegisterAsync(Register("ADMIN@test.com")));
    }

    private static RegisterRequest Register(string email) => new()
    {
        ShopName = "Shine & Fold",
        OwnerName = "Rahim Ahmed",
        Email = email,
        Phone = "+880 1712-345678",
        Password = "secret123",
        City = "Dhaka"
    };

    [Fact]
    public async Task Register_CreatesShopOwnerAndReturnsToken()
    {
        var service = Create(out var db, out _);

        var result = await service.RegisterAsync(Register("owner@test.com"));

        Assert.Equal("signed-token", result.Token);
        Assert.Equal("Shine & Fold", result.Shop.Name);
        Assert.Single(db.Shops);
        Assert.Single(db.Users);
        Assert.NotEqual("secret123", db.Users.Single().PasswordHash);
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmail()
    {
        var service = Create(out _, out _);
        await service.RegisterAsync(Register("owner@test.com"));

        await Assert.ThrowsAsync<AppException>(() => service.RegisterAsync(Register("OWNER@test.com")));
    }

    [Fact]
    public async Task Register_FallsBackToStarterForInvalidPlan()
    {
        var service = Create(out _, out _);
        var request = Register("owner@test.com");
        request.Plan = "Enterprise";

        var result = await service.RegisterAsync(request);

        Assert.Equal("Starter", result.Shop.Plan);
    }

    [Fact]
    public async Task Login_AcceptsValidCredentialsAndRejectsWrongPassword()
    {
        var service = Create(out _, out _);
        await service.RegisterAsync(Register("owner@test.com"));

        var result = await service.LoginAsync(new LoginRequest { Email = "owner@test.com", Password = "secret123" });
        Assert.Equal("Rahim Ahmed", result.User.Name);

        await Assert.ThrowsAsync<AppException>(() =>
            service.LoginAsync(new LoginRequest { Email = "owner@test.com", Password = "wrong" }));
    }

    [Fact]
    public async Task Refresh_ConcurrentCallsShareOneRotation()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new TestDbContext(options);
        var shop = new Shop { Name = "S", SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1) };
        var user = new AppUser { Shop = shop, Email = "owner@test.com", IsActive = true, PasswordHash = "hash" };
        db.Users.Add(user);
        db.SaveChanges();

        var tokens = new FakeRefreshTokenStore();
        tokens.Seed("r0", user.Id);
        var cache = new FakeCache();
        var service = new AuthService(db, new FakeTokens(), new FakeAdminSettings(), tokens, cache);

        var first = await service.RefreshAsync("r0");
        var second = await service.RefreshAsync("r0");

        Assert.Equal(first.RefreshToken, second.RefreshToken);
        Assert.False(tokens.IsRevoked(first.RefreshToken));
    }
}

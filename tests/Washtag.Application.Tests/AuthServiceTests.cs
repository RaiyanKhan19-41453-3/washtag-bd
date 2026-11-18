using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Services;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Application.Tests;

public class AuthServiceTests
{
    private static AuthService Create(out TestDbContext db)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        db = new TestDbContext(options);
        return new AuthService(db, new FakeTokens());
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
        var service = Create(out var db);

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
        var service = Create(out _);
        await service.RegisterAsync(Register("owner@test.com"));

        await Assert.ThrowsAsync<AppException>(() => service.RegisterAsync(Register("OWNER@test.com")));
    }

    [Fact]
    public async Task Login_AcceptsValidCredentialsAndRejectsWrongPassword()
    {
        var service = Create(out _);
        await service.RegisterAsync(Register("owner@test.com"));

        var result = await service.LoginAsync(new LoginRequest { Email = "owner@test.com", Password = "secret123" });
        Assert.Equal("Rahim Ahmed", result.User.Name);

        await Assert.ThrowsAsync<AppException>(() =>
            service.LoginAsync(new LoginRequest { Email = "owner@test.com", Password = "wrong" }));
    }
}

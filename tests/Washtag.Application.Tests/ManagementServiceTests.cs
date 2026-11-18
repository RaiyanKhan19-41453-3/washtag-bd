using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Services;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Application.Tests;

public class ManagementServiceTests
{
    private sealed class Harness
    {
        public Guid ShopId { get; } = Guid.NewGuid();
        public TestDbContext Db { get; }

        public Harness()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new TestDbContext(options);
            Db.Shops.Add(new Shop
            {
                Id = ShopId, Name = "Test Shop", Plan = "Starter",
                SubscriptionStatus = SubscriptionStatus.Trial, SubscriptionEndsAt = DateTime.UtcNow.AddDays(10)
            });
            Db.SaveChanges();
        }
    }

    private static StaffService Staff(Harness h) => new(h.Db);
    private static BillingService Billing(Harness h) => new(h.Db);

    [Fact]
    public async Task Staff_CreateHashesPasswordAndScopesAccountToShop()
    {
        var h = new Harness();
        var created = await Staff(h).CreateAsync(h.ShopId, new CreateStaffRequest
        {
            Name = "Sumaiya", Email = "STAFF@TEST.COM", Phone = "+8801", Password = "secret123"
        });

        Assert.Equal("staff@test.com", created.Email);
        Assert.Equal(UserRole.Staff, created.Role);
        var stored = await h.Db.Users.SingleAsync(u => u.Id == created.Id);
        Assert.Equal(h.ShopId, stored.ShopId);
        Assert.NotEqual("secret123", stored.PasswordHash);
    }

    [Fact]
    public async Task Staff_CannotCreateAnOwnerAccount()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<AppException>(() => Staff(h).CreateAsync(h.ShopId, new CreateStaffRequest
        {
            Name = "Boss", Email = "boss@test.com", Password = "secret123", Role = UserRole.Owner
        }));
    }

    [Fact]
    public async Task Staff_DuplicateEmailAcrossShopsIsRejected()
    {
        var h = new Harness();
        h.Db.Users.Add(new AppUser { ShopId = Guid.NewGuid(), Email = "taken@test.com", Name = "Other", PasswordHash = "x" });
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<AppException>(() => Staff(h).CreateAsync(h.ShopId, new CreateStaffRequest
        {
            Name = "Taken", Email = "taken@test.com", Password = "secret123"
        }));
    }

    [Fact]
    public async Task Staff_OwnerCannotBeDeactivated()
    {
        var h = new Harness();
        var ownerId = Guid.NewGuid();
        h.Db.Users.Add(new AppUser { Id = ownerId, ShopId = h.ShopId, Email = "owner@test.com", Name = "Owner", Role = UserRole.Owner, PasswordHash = "x" });
        await h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<AppException>(() => Staff(h).DeactivateAsync(h.ShopId, ownerId));
    }

    [Fact]
    public async Task StaffDeactivationRevokesRefreshTokens()
    {
        var h = new Harness();
        var staffId = Guid.NewGuid();
        h.Db.Users.Add(new AppUser
        {
            Id = staffId, ShopId = h.ShopId, Email = "staff@test.com", Name = "Staff",
            Role = UserRole.Staff, PasswordHash = "x", IsActive = true
        });
        h.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = staffId, TokenHash = "hash", ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await h.Db.SaveChangesAsync();

        await Staff(h).DeactivateAsync(h.ShopId, staffId);

        Assert.NotNull((await h.Db.RefreshTokens.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Billing_OverviewIncludesOutstandingInvoices()
    {
        var h = new Harness();
        h.Db.Invoices.Add(new Invoice
        {
            ShopId = h.ShopId, InvoiceNumber = "INV-1", Amount = 500, Status = InvoiceStatus.Pending,
            PeriodFrom = DateTime.UtcNow.AddDays(-30), PeriodTo = DateTime.UtcNow
        });
        await h.Db.SaveChangesAsync();

        var overview = await Billing(h).GetOverviewAsync(h.ShopId);

        Assert.Equal(500, overview.MonthlyPrice);
        Assert.Equal(500, overview.Outstanding);
        Assert.Single(overview.Invoices);
    }

    [Fact]
    public async Task Billing_ExpiredPendingInvoiceBecomesOverdue()
    {
        var h = new Harness();
        h.Db.Invoices.Add(new Invoice
        {
            ShopId = h.ShopId, InvoiceNumber = "INV-OLD", Amount = 500,
            Status = InvoiceStatus.Pending,
            PeriodFrom = DateTime.UtcNow.AddDays(-40),
            PeriodTo = DateTime.UtcNow.AddDays(-10)
        });
        await h.Db.SaveChangesAsync();

        var overview = await Billing(h).GetOverviewAsync(h.ShopId);

        Assert.Equal(InvoiceStatus.Overdue, overview.Invoices.Single().Status);
        Assert.Equal(SubscriptionStatus.PastDue, overview.Status);
    }

    [Fact]
    public async Task Billing_MarkPaidActivatesSubscriptionWhenNoInvoicesRemain()
    {
        var h = new Harness();
        var invoice = new Invoice { ShopId = h.ShopId, InvoiceNumber = "INV-2", Amount = 500, Status = InvoiceStatus.Pending };
        h.Db.Invoices.Add(invoice);
        await h.Db.SaveChangesAsync();

        var paid = await Billing(h).MarkPaidAsync(h.ShopId, invoice.Id,
            new MarkInvoicePaidRequest { Method = PaymentMethod.Bkash, Reference = "BK-1" });

        Assert.Equal(InvoiceStatus.Paid, paid.Status);
        Assert.NotNull(paid.PaidAt);
        var shop = await h.Db.Shops.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, shop.SubscriptionStatus);
        await Assert.ThrowsAsync<AppException>(() => Billing(h).MarkPaidAsync(h.ShopId, invoice.Id, new MarkInvoicePaidRequest()));
    }

    [Fact]
    public async Task Billing_SettlingLastInvoiceStartsNextPeriod()
    {
        var h = new Harness();
        var invoice = new Invoice
        {
            ShopId = h.ShopId, InvoiceNumber = "INV-NEXT", Amount = 500,
            Status = InvoiceStatus.Pending, PeriodFrom = DateTime.UtcNow.AddDays(-10),
            PeriodTo = DateTime.UtcNow.AddDays(20)
        };
        h.Db.Invoices.Add(invoice);
        await h.Db.SaveChangesAsync();

        await Billing(h).MarkPaidAsync(h.ShopId, invoice.Id, new MarkInvoicePaidRequest());

        var shop = await h.Db.Shops.SingleAsync();
        Assert.True(shop.SubscriptionEndsAt > DateTime.UtcNow.AddDays(25));
        Assert.Equal(1, await h.Db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Pending));
    }

    [Fact]
    public async Task Billing_ChangePlanUpdatesPendingInvoiceAmounts()
    {
        var h = new Harness();
        var invoice = new Invoice
        {
            ShopId = h.ShopId, InvoiceNumber = "INV-3", Amount = 500,
            Status = InvoiceStatus.Pending,
            PeriodFrom = DateTime.UtcNow.AddDays(-10),
            PeriodTo = DateTime.UtcNow.AddDays(20)
        };
        h.Db.Invoices.Add(invoice);
        await h.Db.SaveChangesAsync();

        var result = await Billing(h).ChangePlanAsync(h.ShopId, new ChangePlanRequest { Plan = "pro" });

        Assert.Equal("Pro", result.Plan);
        Assert.Equal(1000, result.MonthlyPrice);
        Assert.Equal(1000, (await h.Db.Invoices.SingleAsync()).Amount);
        await Assert.ThrowsAsync<AppException>(() => Billing(h).ChangePlanAsync(h.ShopId, new ChangePlanRequest { Plan = "Enterprise" }));
    }

    [Fact]
    public async Task Admin_StatsAggregateAcrossShops()
    {
        var h = new Harness();
        h.Db.Orders.Add(new Order
        {
            ShopId = h.ShopId, CustomerName = "A", CustomerPhone = "1", OrderNumber = "WT-00001",
            TotalAmount = 200, PaidAmount = 120, Status = OrderStatus.Washing
        });
        await h.Db.SaveChangesAsync();

        var stats = await new AdminService(h.Db).GetStatsAsync();

        Assert.Equal(1, stats.ShopCount);
        Assert.Equal(1, stats.OrderCount);
        Assert.Equal(120, stats.Collected);
        Assert.Single(stats.Shops);
        Assert.Equal("Test Shop", stats.Shops[0].ShopName);
    }

    [Fact]
    public async Task Auth_RefreshRotatesTokenAndOldTokenCannotBeReused()
    {
        var h = new Harness();
        var store = new FakeRefreshTokenStore();
        var auth = new AuthService(h.Db, new FakeTokens(), new FakeAdminSettings(), store);
        var login = await auth.RegisterAsync(new RegisterRequest
        {
            ShopName = "Shop", OwnerName = "Owner", Email = "owner@test.com", Phone = "1", Password = "secret123"
        });
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        store.Seed(login.User.Id);

        var refreshed = await auth.RefreshAsync(login.RefreshToken);

        Assert.False(string.IsNullOrEmpty(refreshed.RefreshToken));
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        await Assert.ThrowsAsync<AppException>(() => auth.RefreshAsync(login.RefreshToken));
    }

    [Fact]
    public async Task Auth_ChangePasswordRevokesExistingRefreshTokens()
    {
        var h = new Harness();
        var auth = new AuthService(h.Db, new FakeTokens(), new FakeAdminSettings(), new FakeRefreshTokenStore());
        var login = await auth.RegisterAsync(new RegisterRequest
        {
            ShopName = "Shop", OwnerName = "Owner", Email = "owner@test.com", Phone = "1", Password = "secret123"
        });
        var userId = login.User.Id;

        await auth.ChangePasswordAsync(userId, new ChangePasswordRequest { CurrentPassword = "secret123", NewPassword = "newpass123" });

        await Assert.ThrowsAsync<AppException>(() => auth.RefreshAsync(login.RefreshToken));
        await Assert.ThrowsAsync<AppException>(() => auth.LoginAsync(new LoginRequest { Email = "owner@test.com", Password = "secret123" }));
        Assert.NotNull(await auth.LoginAsync(new LoginRequest { Email = "owner@test.com", Password = "newpass123" }));
    }
}

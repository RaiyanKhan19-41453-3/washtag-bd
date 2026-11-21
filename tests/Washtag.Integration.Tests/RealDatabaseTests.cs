using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;
using Washtag.Infrastructure.Data;
using Xunit;

namespace Washtag.Integration.Tests;

[Collection("database")]
public class RealDatabaseTests
{
    private readonly DatabaseFixture _fixture;
    public RealDatabaseTests(DatabaseFixture fixture) => _fixture = fixture;

    private static string Hash(string raw) => Convert.ToBase64String(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    [Fact]
    public async Task Migrations_ApplyOnARealPostgresDatabase()
    {
        using var scope = _fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains("20261119131000_AddReportingIndexes", applied);
        Assert.True(await db.Database.CanConnectAsync());
    }

    [Fact]
    public async Task TwoConcurrentSettlements_CreateExactlyOneRenewalAndDoNotShrinkTheTerm()
    {
        var shop = await _fixture.NewShopAsync("Pro", DateTime.UtcNow.AddMonths(11));
        var invoices = new List<Invoice>();
        using (var scope = _fixture.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 2; i++)
            {
                invoices.Add(new Invoice
                {
                    ShopId = shop.Id, InvoiceNumber = $"INV-RACE-{Guid.NewGuid():N}",
                    PeriodFrom = DateTime.UtcNow.AddDays(-10), PeriodTo = DateTime.UtcNow.AddDays(20),
                    Amount = 800, Status = InvoiceStatus.Pending, CreatedAt = DateTime.UtcNow
                });
            }
            db.Invoices.AddRange(invoices);
            await db.SaveChangesAsync();
        }

        using var work = _fixture.Scope();
        var store = work.ServiceProvider.GetRequiredService<IInvoiceStore>();
        var results = await Task.WhenAll(invoices.Select(i =>
            store.SettleAsync(shop.Id, i.Id, PaymentMethod.Cash, null)));

        Assert.All(results, Assert.True);
        using var check = _fixture.Scope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await dbCheck.Shops.SingleAsync(s => s.Id == shop.Id);
        var all = await dbCheck.Invoices.Where(i => i.ShopId == shop.Id).ToListAsync();
        Assert.Equal(SubscriptionStatus.Active, updated.SubscriptionStatus);
        Assert.True(updated.SubscriptionEndsAt >= DateTime.UtcNow.AddMonths(10));
        Assert.Equal(3, all.Count); // two paid + exactly one renewal
        Assert.Single(all, i => i.Status == InvoiceStatus.Pending);
    }

    [Fact]
    public async Task ManyConcurrentSettlements_CreateExactlyOneRenewal()
    {
        const int count = 8;
        var shop = await _fixture.NewShopAsync("Pro", DateTime.UtcNow.AddMonths(11));
        var invoices = new List<Invoice>();
        using (var scope = _fixture.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < count; i++)
            {
                invoices.Add(new Invoice
                {
                    ShopId = shop.Id, InvoiceNumber = $"INV-STRESS-{Guid.NewGuid():N}",
                    PeriodFrom = DateTime.UtcNow.AddDays(-10), PeriodTo = DateTime.UtcNow.AddDays(20),
                    Amount = 800, Status = InvoiceStatus.Pending, CreatedAt = DateTime.UtcNow
                });
            }
            db.Invoices.AddRange(invoices);
            await db.SaveChangesAsync();
        }

        using var work = _fixture.Scope();
        var store = work.ServiceProvider.GetRequiredService<IInvoiceStore>();
        var results = await Task.WhenAll(invoices.Select(i =>
            store.SettleAsync(shop.Id, i.Id, PaymentMethod.Cash, null)));

        Assert.All(results, Assert.True);
        using var check = _fixture.Scope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await dbCheck.Shops.SingleAsync(s => s.Id == shop.Id);
        var all = await dbCheck.Invoices.Where(i => i.ShopId == shop.Id).ToListAsync();
        Assert.Equal(SubscriptionStatus.Active, updated.SubscriptionStatus);
        Assert.True(updated.SubscriptionEndsAt >= DateTime.UtcNow.AddMonths(10));
        Assert.Equal(count + 1, all.Count); // all paid + exactly one renewal
        Assert.Single(all, i => i.Status == InvoiceStatus.Pending);
    }

    [Fact]
    public async Task ConcurrentPartialPayments_NeverExceedTheOrderBalance()
    {
        var shop = await _fixture.NewShopAsync();
        Guid orderId;
        using (var scope = _fixture.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order
            {
                ShopId = shop.Id, OrderNumber = $"WT-{Guid.NewGuid():N}".ToUpperInvariant(),
                PublicToken = Guid.NewGuid(), CustomerName = "Customer", CustomerPhone = "1",
                TotalAmount = 100, PaidAmount = 0, Status = OrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        using var work = _fixture.Scope();
        var payments = work.ServiceProvider.GetRequiredService<IPaymentStore>();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            payments.RecordAsync(shop.Id, orderId, 80, PaymentMethod.Cash, null, "tester")));

        Assert.Equal(1, results.Count(r => r));
        using var check = _fixture.Scope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(80, (await dbCheck.Orders.SingleAsync(o => o.Id == orderId)).PaidAmount);
    }

    [Fact]
    public async Task SameIdempotencyKeyUnderConcurrency_CreatesOneOrderWithOneCustomerAndOneVector()
    {
        var shop = await _fixture.NewShopAsync();
        var key = $"integration-{Guid.NewGuid()}";
        var request = new CreateOrderRequest
        {
            CustomerName = "Concurrent Customer", CustomerPhone = "+8801",
            Items = new List<CreateOrderItemRequest> { new() { Name = "Shirt", Quantity = 1, UnitPrice = 250 } }
        };

        // Each HTTP request gets its own DI scope in ASP.NET Core - model that here.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using var scope = _fixture.Scope();
            return await scope.ServiceProvider.GetRequiredService<IOrderService>()
                .CreateOrderAsync(shop.Id, request, "tester", key);
        }));

        Assert.Single(responses, r => !r.Replayed);
        Assert.All(responses, r => Assert.Equal(responses[0].Id, r.Id));

        using var check = _fixture.Scope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbCheck.Orders.CountAsync(o => o.ShopId == shop.Id));
        Assert.Equal(1, await dbCheck.Customers.CountAsync(c => c.ShopId == shop.Id));
        // One order document + one customer document, both idempotently upserted.
        Assert.Equal(2, await check.ServiceProvider.GetRequiredService<IVectorStore>().CountAsync(shop.Id));
    }

    [Fact]
    public async Task RefreshRotation_ConcurrentCallsWinOnceAndOldReplayRevokesTheFamily()
    {
        var shop = await _fixture.NewShopAsync();
        Guid userId;
        using (var scope = _fixture.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new AppUser
            {
                Shop = db.Shops.First(s => s.Id == shop.Id), Name = "Owner",
                Email = $"user-{Guid.NewGuid()}@test.com", PasswordHash = "hash", IsActive = true
            };
            db.Users.Add(user);
            var token = new RefreshToken
            {
                UserId = user.Id, TokenHash = Hash("r0"),
                ExpiresAt = DateTime.UtcNow.AddDays(30), CreatedAt = DateTime.UtcNow
            };
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using var work = _fixture.Scope();
        var store = work.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(i =>
            store.RotateAsync(Hash("r0"), Hash($"r{i + 1}"), DateTime.UtcNow.AddDays(30))));
        Assert.Equal(1, attempts.Count(a => a.HasValue));

        string successor;
        using (var peek = _fixture.Scope())
        {
            var db = peek.ServiceProvider.GetRequiredService<AppDbContext>();
            successor = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.TokenHash == Hash("r0"))
                .Select(t => t.ReplacedBy!).SingleAsync();
        }
        Assert.NotNull(successor);

        // Age the revoked token to simulate a real theft replay after the grace window.
        using (var age = _fixture.Scope())
        {
            var db = age.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = await db.RefreshTokens.SingleAsync(t => t.TokenHash == Hash("r0"));
            old.RevokedAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Null(await store.RotateAsync(Hash("r0"), Hash("stolen"), DateTime.UtcNow.AddDays(30)));
        using var verify = _fixture.Scope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var child = await dbVerify.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == successor);
        Assert.NotNull(child);
        Assert.NotNull(child!.RevokedAt);
    }

    [Fact]
    public async Task VectorStore_IsStrictlyTenantScoped()
    {
        var a = await _fixture.NewShopAsync();
        var b = await _fixture.NewShopAsync();
        using var scope = _fixture.Scope();
        var vectors = scope.ServiceProvider.GetRequiredService<IVectorStore>();
        var embedding = Enumerable.Repeat(0.1f, 384).ToArray();
        await vectors.UpsertAsync(a.Id, "order", Guid.NewGuid(), "shop A secret order", embedding);
        await vectors.UpsertAsync(b.Id, "order", Guid.NewGuid(), "shop B secret order", embedding);

        var matchesA = await vectors.SearchAsync(a.Id, embedding, 10);
        var matchesB = await vectors.SearchAsync(b.Id, embedding, 10);
        Assert.Single(matchesA);
        Assert.Single(matchesB);
        Assert.Contains("shop A", matchesA[0].Content);
        Assert.Contains("shop B", matchesB[0].Content);
    }

    [Fact]
    public async Task Deadlines_UseEndDateAsTheSingleEntitlementSource()
    {
        var expired = await _fixture.NewShopAsync(endsAt: DateTime.UtcNow.AddDays(-1));
        var healthy = await _fixture.NewShopAsync(endsAt: DateTime.UtcNow.AddMonths(1));
        using (var seed = _fixture.Scope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.Add(new Invoice
            {
                ShopId = expired.Id, InvoiceNumber = $"INV-{Guid.NewGuid():N}",
                PeriodFrom = DateTime.UtcNow.AddMonths(-2), PeriodTo = DateTime.UtcNow.AddDays(-1),
                Amount = 500, Status = InvoiceStatus.Pending, CreatedAt = DateTime.UtcNow
            });
            db.Invoices.Add(new Invoice
            {
                ShopId = healthy.Id, InvoiceNumber = $"INV-{Guid.NewGuid():N}",
                PeriodFrom = DateTime.UtcNow, PeriodTo = DateTime.UtcNow.AddMonths(1),
                Amount = 500, Status = InvoiceStatus.Paid, PaidAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var work = _fixture.Scope();
        var store = work.ServiceProvider.GetRequiredService<IInvoiceStore>();
        var result = await store.ApplyDeadlinesAsync(DateTime.UtcNow);

        Assert.True(result.OverdueInvoices >= 1);
        Assert.True(result.ShopsFlagged >= 1);
        using var check = _fixture.Scope();
        var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(SubscriptionStatus.PastDue, (await dbCheck.Shops.SingleAsync(s => s.Id == expired.Id)).SubscriptionStatus);
        Assert.NotEqual(SubscriptionStatus.PastDue, (await dbCheck.Shops.SingleAsync(s => s.Id == healthy.Id)).SubscriptionStatus);
    }

    [Fact]
    public async Task Database_EnforcesUniqueEmailConstraint()
    {
        var shop = await _fixture.NewShopAsync();
        using var scope = _fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Shops.Add(new Shop
        {
            Name = "Duplicate", OwnerName = "Owner", Email = shop.Email, Phone = "1",
            SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1)
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<Npgsql.PostgresException>(ex.InnerException);
        Assert.Equal("23505", ((Npgsql.PostgresException)ex.InnerException!).SqlState);
    }

    [Fact]
    public async Task ServiceCatalog_CaseVariantRaceCreatesOnlyOneRow()
    {
        var shop = await _fixture.NewShopAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            using var scope = _fixture.Scope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IServiceCatalogService>().CreateAsync(
                    shop.Id, new CreateServiceRequest
                    {
                        Name = i % 2 == 0 ? "Dry Clean" : "dry clean", DefaultPrice = 250
                    });
                return "ok";
            }
            catch (AppException) { return "duplicate"; }
            catch (DbUpdateException) { return "db-duplicate"; }
        }));

        using var check = _fixture.Scope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.ShopServices.CountAsync(s => s.ShopId == shop.Id);
        Assert.Equal(1, count);
        Assert.Single(results, r => r == "ok");
    }

    [Fact]
    public async Task Reports_ByDayCollectionsReconcileWithHeadline()
    {
        var shop = await _fixture.NewShopAsync();
        var from = DateTime.UtcNow.AddDays(-3);
        var to = DateTime.UtcNow.AddDays(1);
        Guid orderId;
        using (var seed = _fixture.Scope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order
            {
                ShopId = shop.Id, OrderNumber = "WT-REPORT", PublicToken = Guid.NewGuid(),
                CustomerName = "Report Customer", CustomerPhone = "1", TotalAmount = 500,
                PaidAmount = 500, Status = OrderStatus.Received, PaymentStatus = PaymentStatus.Paid,
                CreatedAt = from.AddDays(1), UpdatedAt = from.AddDays(1)
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
            db.Payments.Add(new Payment
            {
                OrderId = order.Id, Amount = 500, Method = PaymentMethod.Bkash,
                RecordedBy = "tester", CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var work = _fixture.Scope();
        var report = await work.ServiceProvider.GetRequiredService<IReportService>()
            .DailyAsync(shop.Id, from, to);
        Assert.Equal(500, report.Collected);
        Assert.Equal(report.Collected, report.ByDay.Sum(d => d.Collected));
    }

    [Fact]
    public async Task RefreshStore_WrongSessionRevokesBoundTokenOnRealPostgres()
    {
        var shop = await _fixture.NewShopAsync();
        var suffix = Guid.NewGuid().ToString("N");
        static string Hash(string raw) => Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        using (var seed = _fixture.Scope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new AppUser
            {
                Shop = db.Shops.First(s => s.Id == shop.Id), Name = "Owner",
                Email = $"bound-{Guid.NewGuid()}@test.com", PasswordHash = "hash", IsActive = true
            };
            db.Users.Add(user);
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id, TokenHash = Hash("r0-" + suffix), SessionHash = Hash("owner-session"),
                ExpiresAt = DateTime.UtcNow.AddDays(30), CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var work = _fixture.Scope();
        var store = work.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        Assert.Null(await store.RotateAsync(Hash("r0-" + suffix), Hash("attacker-" + suffix), DateTime.UtcNow.AddDays(30), Hash("attacker")));
        // A wrong-secret attempt must revoke the victim's bound token and its whole lineage.
        Assert.False(await store.IsActiveAsync(Hash("r0-" + suffix)));
        Assert.False(await store.IsActiveAsync(Hash("attacker-" + suffix)));
    }

    [Fact]
    public async Task ServiceCatalog_DeletedServicesAreNotReseeded()
    {
        var shop = await _fixture.NewShopAsync();
        using var work = _fixture.Scope();
        var catalog = work.ServiceProvider.GetRequiredService<IServiceCatalogService>();
        var services = await catalog.ListAsync(shop.Id);
        Assert.Equal(3, services.Count);
        foreach (var s in services) await catalog.DeleteAsync(shop.Id, s.Id);
        Assert.Empty(await catalog.ListAsync(shop.Id));
    }
}

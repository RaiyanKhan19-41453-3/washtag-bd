using Microsoft.EntityFrameworkCore;
using Washtag.Application;
using Washtag.Application.Services;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Xunit;

namespace Washtag.Application.Tests;

public class MaintenanceServiceTests
{
    private sealed class Harness
    {
        public Guid ShopId { get; } = Guid.NewGuid();
        public TestDbContext Db { get; }
        public FakeRefreshTokenStore Tokens { get; } = new();
        public FakeIndexer Indexer { get; } = new();
        public FakeInvoiceStore Invoices { get; private set; } = null!;
        public MaintenanceOptions Options { get; } = new();

        public Harness()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new TestDbContext(options);
            Invoices = new FakeInvoiceStore(Db);
            Db.Shops.Add(new Shop
            {
                Id = ShopId, Name = "Test Shop", Plan = "Starter",
                SubscriptionStatus = SubscriptionStatus.Active, SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1)
            });
            Db.SaveChanges();
        }

        public MaintenanceService Service() => new(Db, Tokens, Indexer, Invoices, Options);
    }

    [Fact]
    public async Task MarksOverdueInvoicesAndFlagsShopPastDue()
    {
        var h = new Harness();
        h.Db.Shops.Single().SubscriptionEndsAt = DateTime.UtcNow.AddDays(-1);
        h.Db.Invoices.Add(new Invoice
        {
            ShopId = h.ShopId, InvoiceNumber = "INV-OLD",
            PeriodFrom = DateTime.UtcNow.AddMonths(-2), PeriodTo = DateTime.UtcNow.AddDays(-1),
            Amount = 500, Status = InvoiceStatus.Pending
        });
        h.Db.SaveChanges();

        var result = await h.Service().RunAsync();

        Assert.Equal(1, result.OverdueInvoices);
        Assert.Equal(1, result.ShopsMarkedPastDue);
        Assert.Equal(InvoiceStatus.Overdue, h.Db.Invoices.Single().Status);
        Assert.Equal(SubscriptionStatus.PastDue, h.Db.Shops.Single().SubscriptionStatus);
    }

    [Fact]
    public async Task ExpiresSubscriptionsPastTheirEndDate()
    {
        var h = new Harness();
        h.Db.Shops.Single().SubscriptionEndsAt = DateTime.UtcNow.AddDays(-1);
        h.Db.SaveChanges();

        var result = await h.Service().RunAsync();

        Assert.Equal(1, result.ExpiredSubscriptions);
        Assert.Equal(SubscriptionStatus.PastDue, h.Db.Shops.Single().SubscriptionStatus);
    }

    [Fact]
    public async Task LeavesCancelledAndSuspendedShopsUntouched()
    {
        var h = new Harness();
        var cancelled = h.Db.Shops.Single();
        cancelled.SubscriptionStatus = SubscriptionStatus.Cancelled;
        cancelled.SubscriptionEndsAt = DateTime.UtcNow.AddDays(-1);
        h.Db.Shops.Add(new Shop
        {
            Id = Guid.NewGuid(), Name = "Suspended", SubscriptionStatus = SubscriptionStatus.Active,
            SubscriptionEndsAt = DateTime.UtcNow.AddDays(-1), IsActive = false
        });
        h.Db.SaveChanges();

        var result = await h.Service().RunAsync();

        // Suspended shops are skipped on purpose so an admin reactivation restores access.
        Assert.Equal(0, result.ExpiredSubscriptions);
        Assert.Equal(SubscriptionStatus.Cancelled, cancelled.SubscriptionStatus);
        Assert.Equal(SubscriptionStatus.Active, h.Db.Shops.Single(s => s.Name == "Suspended").SubscriptionStatus);
    }

    [Fact]
    public async Task PrunesAssistantMessagesOlderThanRetention()
    {
        var h = new Harness();
        h.Options.ChatRetentionDays = 30;
        h.Db.AssistantMessages.AddRange(
            new AssistantMessage { ShopId = h.ShopId, UserId = Guid.NewGuid(), Text = "old", CreatedAt = DateTime.UtcNow.AddDays(-31) },
            new AssistantMessage { ShopId = h.ShopId, UserId = Guid.NewGuid(), Text = "recent", CreatedAt = DateTime.UtcNow.AddDays(-1) });
        h.Db.SaveChanges();

        var result = await h.Service().RunAsync();

        Assert.Equal(1, result.PrunedMessages);
        Assert.Single(h.Db.AssistantMessages);
        Assert.Equal("recent", h.Db.AssistantMessages.Single().Text);
    }

    [Fact]
    public async Task PurgesTokensAndReindexesEveryShop()
    {
        var h = new Harness();
        h.Db.Shops.Add(new Shop
        {
            Id = Guid.NewGuid(), Name = "Second Shop",
            SubscriptionStatus = SubscriptionStatus.Active, SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1)
        });
        h.Db.SaveChanges();

        var result = await h.Service().RunAsync();

        Assert.True(result.TokensPurged);
        Assert.Equal(1, h.Tokens.PurgeCalls);
        Assert.Equal(2, result.ReindexedShops);
        Assert.Equal(2, h.Indexer.ReindexCalls);
    }

    [Fact]
    public async Task SecondRunHasNothingNewToDo()
    {
        var h = new Harness();
        h.Db.Invoices.Add(new Invoice
        {
            ShopId = h.ShopId, InvoiceNumber = "INV-OLD",
            PeriodFrom = DateTime.UtcNow.AddMonths(-2), PeriodTo = DateTime.UtcNow.AddDays(-1),
            Amount = 500, Status = InvoiceStatus.Pending
        });
        h.Db.SaveChanges();
        var service = h.Service();

        await service.RunAsync();
        var second = await service.RunAsync();

        Assert.Equal(0, second.OverdueInvoices);
        Assert.Equal(0, second.ExpiredSubscriptions);
        Assert.Equal(0, second.ShopsMarkedPastDue);
        Assert.Equal(0, second.PrunedMessages);
        Assert.Equal(1, second.ReindexedShops);
    }
}

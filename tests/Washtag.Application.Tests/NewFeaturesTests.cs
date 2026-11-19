using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Services;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Application.Tests;

public class NewFeaturesTests
{
    private static (TestDbContext db, Guid shopId) NewDb()
    {
        var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shopId = Guid.NewGuid();
        db.Shops.Add(new Shop { Id = shopId, Name = "Shop", SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1) });
        db.SaveChanges();
        return (db, shopId);
    }

    [Fact]
    public async Task ServiceCatalog_IsScopedToAShopAndRejectsDuplicates()
    {
        var (db, shopId) = NewDb();
        var service = new ServiceCatalogService(db);
        var configured = await db.Shops.SingleAsync(s => s.Id == shopId);
        configured.CatalogueInitialized = true;
        await db.SaveChangesAsync();
        var created = await service.CreateAsync(shopId, new CreateServiceRequest { Name = "Dry Clean", DefaultPrice = 250 });

        Assert.Equal(250, created.DefaultPrice);
        await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(shopId,
            new CreateServiceRequest { Name = "dry clean", DefaultPrice = 100 }));
        // Legacy shops get a one-time default catalogue on first read, and deleting
        // all services later does not trigger a re-seed.
        var (legacyDb, legacyShopId) = NewDb();
        Assert.Equal(3, (await new ServiceCatalogService(legacyDb).ListAsync(legacyShopId)).Count);

        var updated = await service.UpdateAsync(shopId, created.Id,
            new UpdateServiceRequest { Name = "Dry Clean", DefaultPrice = 300, IsActive = false });
        Assert.False(updated.IsActive);
        await service.DeleteAsync(shopId, created.Id);
        Assert.Empty(await service.ListAsync(shopId));
    }

    [Fact]
    public async Task Reports_AggregateOrdersAndProduceCsv()
    {
        var (db, shopId) = NewDb();
        var order = new Order
        {
            ShopId = shopId, OrderNumber = "WT-1", PublicToken = Guid.NewGuid(),
            CustomerName = "A,B \"Customer\"", CustomerPhone = "1", TotalAmount = 200,
            PaidAmount = 120, Status = OrderStatus.Received, PaymentStatus = PaymentStatus.Partial,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        db.SaveChanges();
        db.Payments.Add(new Payment
        {
            OrderId = order.Id, Amount = 120, Method = PaymentMethod.Bkash,
            RecordedBy = "tester", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reports = new ReportService(db);
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);
        var daily = await reports.DailyAsync(shopId, from, to);
        Assert.Equal(1, daily.OrderCount);
        Assert.Equal(120, daily.Collected);
        Assert.Equal(80, daily.Outstanding);
        Assert.Single(daily.ByMethod);

        var csv = System.Text.Encoding.UTF8.GetString(await reports.OrdersCsvAsync(shopId, from, to));
        Assert.Contains("\"A,B \"\"Customer\"\"\"", csv);
        Assert.Contains("WT-1", csv);
    }

    [Fact]
    public async Task DeliveryOrder_RequiresAnAddressAndPersistsIt()
    {
        var (db, shopId) = NewDb();
        var service = new OrderService(db, new FakeNotifier(), new FakeCache(), new FakeQr(),
            new FakeUrls(), new FakeIndexer(), new FakeNumbers(), new FakeCustomerStore(),
            new FakePaymentStore(db), new FakeMessageSender());

        await Assert.ThrowsAsync<AppException>(() => service.CreateOrderAsync(shopId, new CreateOrderRequest
        {
            CustomerName = "Customer", CustomerPhone = "1", DeliveryMode = DeliveryMode.Delivery,
            Items = new List<CreateOrderItemRequest> { new() { Name = "Shirt" } }
        }, "tester"));

        var order = await service.CreateOrderAsync(shopId, new CreateOrderRequest
        {
            CustomerName = "Customer", CustomerPhone = "1", DeliveryMode = DeliveryMode.Delivery,
            DeliveryAddress = "House 12, Road 7",
            Items = new List<CreateOrderItemRequest> { new() { Name = "Shirt" } }
        }, "tester");

        Assert.Equal(DeliveryMode.Delivery, order.DeliveryMode);
        Assert.Equal("House 12, Road 7", order.DeliveryAddress);
    }
}

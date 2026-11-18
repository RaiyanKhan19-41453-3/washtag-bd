using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Services;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Application.Tests;

public class OrderServiceTests
{
    private sealed class Harness
    {
        public Guid ShopId { get; } = Guid.NewGuid();
        public TestDbContext Db { get; }
        public OrderService Service { get; }
        public FakeNotifier Notifier { get; } = new();
        public FakeIndexer Indexer { get; } = new();

        public Harness()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new TestDbContext(options);
            Db.Shops.Add(new Washtag.Domain.Entities.Shop { Id = ShopId, Name = "Test Shop" });
            Db.SaveChanges();
            Service = new OrderService(Db, Notifier, new FakeCache(), new FakeQr(),
                new FakeUrls(), Indexer, new FakeNumbers(), new FakeCustomerStore());
        }

        public Task<OrderDetailDto> NewOrder(string customer = "Test Customer", decimal price = 100, int quantity = 2) =>
            Service.CreateOrderAsync(ShopId, new CreateOrderRequest
            {
                CustomerName = customer,
                CustomerPhone = "+880 1700-000000",
                Items = new List<CreateOrderItemRequest>
                {
                    new() { Name = "Cotton Shirt", Service = "Wash & Fold", Quantity = quantity, UnitPrice = price }
                }
            }, "Tester");
    }

    [Fact]
    public async Task CreateOrder_CalculatesTotalAndStartsAtReceived()
    {
        var h = new Harness();

        var order = await h.NewOrder(price: 150, quantity: 3);

        Assert.Equal(450, order.TotalAmount);
        Assert.Equal(OrderStatus.Received, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Single(order.Items);
        Assert.Single(order.StatusHistory);
        Assert.NotEqual(Guid.Empty, Guid.Parse(order.PublicToken));
        Assert.Equal(1, h.Notifier.Calls);
        Assert.Equal(1, h.Indexer.IndexCalls);
        Assert.Contains("data:image/png", order.QrDataUrl);
    }

    [Fact]
    public async Task UpdateStatus_AppendsTransitionToHistory()
    {
        var h = new Harness();
        var order = await h.NewOrder();

        var updated = await h.Service.UpdateStatusAsync(h.ShopId, order.Id,
            new UpdateStatusRequest { Status = OrderStatus.Washing, Note = "Started wash" }, "Staff");

        Assert.Equal(OrderStatus.Washing, updated.Status);
        Assert.Equal(2, updated.StatusHistory.Count);
        Assert.Equal(OrderStatus.Received, updated.StatusHistory[1].FromStatus);
        Assert.Equal("Started wash", updated.StatusHistory[1].Note);
        Assert.Equal(2, h.Notifier.Calls);
    }

    [Fact]
    public async Task RecordPayment_RejectsOverpayment()
    {
        var h = new Harness();
        var order = await h.NewOrder(price: 200, quantity: 1);

        await Assert.ThrowsAsync<AppException>(() => h.Service.RecordPaymentAsync(h.ShopId, order.Id,
            new RecordPaymentRequest { Amount = 500, Method = PaymentMethod.Cash }, "Tester"));
    }

    [Fact]
    public async Task RecordPayment_MarksPaidAndRejectsSecondPayment()
    {
        var h = new Harness();
        var order = await h.NewOrder(price: 200, quantity: 1);

        var paid = await h.Service.RecordPaymentAsync(h.ShopId, order.Id,
            new RecordPaymentRequest { Amount = 200, Method = PaymentMethod.Bkash, Reference = "BK-1" }, "Tester");

        Assert.Equal(PaymentStatus.Paid, paid.PaymentStatus);
        Assert.Equal(200, paid.PaidAmount);
        await Assert.ThrowsAsync<AppException>(() => h.Service.RecordPaymentAsync(h.ShopId, order.Id,
            new RecordPaymentRequest { Amount = 50 }, "Tester"));
    }

    [Fact]
    public async Task GetOrder_IsTenantScoped()
    {
        var h = new Harness();
        var order = await h.NewOrder();

        await Assert.ThrowsAsync<AppException>(() =>
            h.Service.GetOrderAsync(Guid.NewGuid(), order.Id));
        Assert.NotNull(await h.Service.GetOrderAsync(h.ShopId, order.Id));
    }

    [Fact]
    public async Task DeliveredRequiresFullPayment()
    {
        var h = new Harness();
        var order = await h.NewOrder(price: 200, quantity: 1);
        await h.Service.RecordPaymentAsync(h.ShopId, order.Id,
            new RecordPaymentRequest { Amount = 50, Method = PaymentMethod.Cash }, "Tester");

        await Assert.ThrowsAsync<AppException>(() => h.Service.UpdateStatusAsync(h.ShopId, order.Id,
            new UpdateStatusRequest { Status = OrderStatus.Delivered }, "Tester"));
    }

    [Fact]
    public async Task CancelOrder_MarksOrderCancelled()
    {
        var h = new Harness();
        var order = await h.NewOrder();

        await h.Service.CancelOrderAsync(h.ShopId, order.Id, "Owner");

        var cancelled = await h.Service.GetOrderAsync(h.ShopId, order.Id);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(OrderStatus.Cancelled, cancelled.StatusHistory.Last().ToStatus);
    }
}

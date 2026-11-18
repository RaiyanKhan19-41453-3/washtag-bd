using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Services;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Application.Tests;

public class AssistantServiceTests
{
    private sealed class Harness
    {
        public Guid ShopId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public TestDbContext Db { get; }
        public AssistantService Service { get; }

        public Harness()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new TestDbContext(options);
            Service = new AssistantService(Db, new FakeVectors(), new FakeAi(), new FakeIndexer());
        }

        public void AddOrder(string customer, OrderStatus status, decimal total, decimal paid, DateTime created)
        {
            Db.Orders.Add(new Order
            {
                ShopId = ShopId,
                OrderNumber = $"WT-{Db.Orders.Count() + 1:D5}",
                CustomerName = customer,
                CustomerPhone = "+880 1700-000000",
                Status = status,
                TotalAmount = total,
                PaidAmount = paid,
                PaymentStatus = paid >= total ? PaymentStatus.Paid : paid > 0 ? PaymentStatus.Partial : PaymentStatus.Unpaid,
                CreatedAt = created,
                UpdatedAt = created
            });
            Db.SaveChanges();
        }
    }

    [Fact]
    public async Task Ask_UnpaidQuestion_ReturnsGroundedTotal()
    {
        var h = new Harness();
        h.AddOrder("Mr. Rahman", OrderStatus.Washing, 500, 100, DateTime.UtcNow.AddDays(-2));
        h.AddOrder("Tasnim", OrderStatus.Ironing, 300, 300, DateTime.UtcNow.AddDays(-1));

        var answer = await h.Service.AskAsync(h.ShopId, h.UserId,"What is the total unpaid bill for Mr. Rahman?");

        Assert.Equal("unpaid", answer.Intent);
        Assert.Contains("৳400", answer.Answer);
        Assert.Contains("Mr. Rahman", answer.Answer);
        Assert.False(answer.AiPhrased);
    }

    [Fact]
    public async Task Ask_PickupDelay_UsesDayThreshold()
    {
        var h = new Harness();
        h.AddOrder("Old Customer", OrderStatus.ReadyForPickup, 200, 0, DateTime.UtcNow.AddDays(-9));
        h.AddOrder("Fresh Customer", OrderStatus.ReadyForPickup, 200, 0, DateTime.UtcNow.AddDays(-1));

        var answer = await h.Service.AskAsync(h.ShopId, h.UserId,
            "Show me all customers who haven't picked up their clothes in 5 days.");

        Assert.Equal("pickup", answer.Intent);
        Assert.Contains("Old Customer", answer.Answer);
        Assert.Contains("1 order(s)", answer.Answer);
        Assert.DoesNotContain("Fresh Customer", answer.Answer);
    }

    [Fact]
    public async Task Ask_EnglishAndBanglish_ResolveToSamePickupIntent()
    {
        var h = new Harness();
        h.AddOrder("Nadia", OrderStatus.ReadyForPickup, 200, 0, DateTime.UtcNow.AddDays(-8));

        var english = await h.Service.AskAsync(h.ShopId, h.UserId,"orders waiting for pickup for 5 days");
        var banglish = await h.Service.AskAsync(h.ShopId, h.UserId,"kapor 5 din dhore porchha?");

        Assert.Equal("pickup", english.Intent);
        Assert.Equal("pickup", banglish.Intent);
        Assert.Contains("Nadia", banglish.Answer);
    }

    [Fact]
    public async Task Ask_MonthCollectionQuestion_UsesRevenueIntent()
    {
        var h = new Harness();
        h.AddOrder("Tasnim", OrderStatus.Delivered, 500, 500, DateTime.UtcNow.AddDays(-2));

        var answer = await h.Service.AskAsync(h.ShopId, h.UserId, "this month collection?");

        Assert.Equal("revenue", answer.Intent);
        Assert.Contains("this month", answer.Answer);
    }

    [Fact]
    public async Task Ask_BanglishCustomerNameAtStart_IsExtracted()
    {
        var h = new Harness();
        h.AddOrder("Tasnim Rahman", OrderStatus.Washing, 500, 100, DateTime.UtcNow.AddDays(-1));
        h.AddOrder("Other Customer", OrderStatus.Washing, 900, 0, DateTime.UtcNow.AddDays(-1));

        var answer = await h.Service.AskAsync(h.ShopId, h.UserId, "Tasnim Rahman er baki?");

        Assert.Equal("unpaid", answer.Intent);
        Assert.Contains("Tasnim Rahman", answer.Answer);
        Assert.Contains("৳400", answer.Answer);
        Assert.DoesNotContain("Other Customer", answer.Answer);
    }

    [Fact]
    public async Task Ask_BengaliScriptQuestion_ResolvesToUnpaidIntent()
    {
        var h = new Harness();
        h.AddOrder("Rahman", OrderStatus.Washing, 500, 100, DateTime.UtcNow.AddDays(-1));

        var answer = await h.Service.AskAsync(h.ShopId, h.UserId, "আমার বাকি টাকা কত?");

        Assert.Equal("unpaid", answer.Intent);
        Assert.Contains("৳400", answer.Answer);
    }

    [Fact]
    public async Task Ask_WithNoOrders_ReturnsSafeEmptyAnswer()
    {
        var h = new Harness();

        var answer = await h.Service.AskAsync(h.ShopId, h.UserId,"baki taka koto?");

        Assert.Equal("unpaid", answer.Intent);
        Assert.Contains("no unpaid balances", answer.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(answer.Sources);
    }

    [Fact]
    public async Task Ask_PersistsConversationAndFeedback()
    {
        var h = new Harness();
        var answer = await h.Service.AskAsync(h.ShopId, h.UserId, "baki taka koto?");

        var history = await h.Service.GetHistoryAsync(h.ShopId, h.UserId, 50);

        Assert.NotEqual(Guid.Empty, answer.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal(answer.Id, history[1].Id);

        await h.Service.SubmitFeedbackAsync(h.ShopId, h.UserId,
            new AssistantFeedbackRequest { MessageId = answer.Id, Rating = 1 });
        var rated = await h.Service.GetHistoryAsync(h.ShopId, h.UserId, 50);
        Assert.Equal(1, rated[1].Rating);

        await h.Service.ClearHistoryAsync(h.ShopId, h.UserId);
        Assert.Empty(await h.Service.GetHistoryAsync(h.ShopId, h.UserId, 50));
    }

    [Fact]
    public async Task Feedback_OnUserMessageIsRejected()
    {
        var h = new Harness();
        await h.Service.AskAsync(h.ShopId, h.UserId, "baki taka koto?");
        var history = await h.Service.GetHistoryAsync(h.ShopId, h.UserId, 50);
        var userMessage = history.First(m => m.Role == "user");

        Assert.Equal(Guid.Empty, userMessage.Id);
        await Assert.ThrowsAsync<AppException>(() => h.Service.SubmitFeedbackAsync(h.ShopId, h.UserId,
            new AssistantFeedbackRequest { MessageId = userMessage.Id, Rating = 1 }));
    }

    [Fact]
    public async Task History_WithOddLimitStillReturnsCompleteExchange()
    {
        var h = new Harness();
        await h.Service.AskAsync(h.ShopId, h.UserId, "baki taka koto?");

        var history = await h.Service.GetHistoryAsync(h.ShopId, h.UserId, 1);

        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("assistant", history[1].Role);
    }
}

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Washtag.Api.Controllers;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;
using Xunit;

namespace Washtag.Api.Tests;

public class ControllerTests
{
    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public bool Result { get; set; } = true;
        public string? LastKey { get; private set; }
        public Guid LastShopId { get; private set; }
        public int Calls { get; private set; }

        public Task<bool> TryClaimAsync(string key, Guid shopId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastKey = key;
            LastShopId = shopId;
            return Task.FromResult(Result);
        }

        public Task ReleaseAsync(string key, Guid shopId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static void Authenticate(ControllerBase controller, Guid shopId, bool isAdmin = false)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("shopId", shopId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Owner"),
            new Claim(ClaimTypes.Role, "Owner"),
            new Claim("isAdmin", isAdmin ? "true" : "false")
        }, "Test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task OrdersList_UsesShopIdFromJwtClaim()
    {
        var shopId = Guid.NewGuid();
        var service = new Mock<IOrderService>();
        service.Setup(s => s.GetOrdersAsync(shopId, null, null, 1, 20))
            .ReturnsAsync(new PagedResult<OrderListDto> { Total = 0 });
        var controller = new OrdersController(service.Object, new FakeIdempotencyStore());
        Authenticate(controller, shopId);

        var result = await controller.List(null, null, 1, 20);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<PagedResult<OrderListDto>>(ok.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task AssistantAsk_ReturnsGroundedAnswer()
    {
        var shopId = Guid.NewGuid();
        var expected = new AssistantAnswerDto { Answer = "৳5410 unpaid", Intent = "unpaid" };
        var service = new Mock<IAssistantService>();
        service.Setup(s => s.AskAsync(shopId, It.IsAny<Guid>(), "baki taka koto?", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new AssistantController(service.Object);
        Authenticate(controller, shopId);

        var result = await controller.Ask(new AssistantAskRequest { Question = "baki taka koto?" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task AssistantAsk_RejectsBlankQuestion()
    {
        var service = new Mock<IAssistantService>();
        var controller = new AssistantController(service.Object);
        Authenticate(controller, Guid.NewGuid());

        var result = await controller.Ask(new AssistantAskRequest { Question = "   " });

        Assert.IsType<BadRequestObjectResult>(result);
        service.Verify(s => s.AskAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void UndefinedEnumValuesFailDtoValidation()
    {
        var badStatus = new UpdateStatusRequest { Status = (OrderStatus)999 };
        var badMethod = new RecordPaymentRequest { Amount = 100, Method = (PaymentMethod)999 };

        Assert.False(Validator.TryValidateObject(badStatus, new ValidationContext(badStatus), null, true));
        Assert.False(Validator.TryValidateObject(badMethod, new ValidationContext(badMethod), null, true));
    }

    [Fact]
    public async Task PublicTrack_ReturnsNotFoundForUnknownToken()
    {
        var service = new Mock<IOrderService>();
        service.Setup(s => s.TrackAsync("unknown"))
            .ReturnsAsync((PublicTrackDto?)null);
        var controller = new PublicController(service.Object);

        var result = await controller.Track("unknown");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreateWithDuplicateIdempotencyKeyReturnsConflict()
    {
        var shopId = Guid.NewGuid();
        var service = new Mock<IOrderService>();
        var store = new FakeIdempotencyStore { Result = false };
        var controller = new OrdersController(service.Object, store);
        Authenticate(controller, shopId);
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "duplicate-key";

        var result = await controller.Create(new CreateOrderRequest
        {
            CustomerName = "X", CustomerPhone = "1",
            Items = new List<CreateOrderItemRequest> { new() { Name = "Shirt" } }
        });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("duplicate-key", store.LastKey);
        Assert.Equal(shopId, store.LastShopId);
        service.Verify(s => s.CreateOrderAsync(It.IsAny<Guid>(), It.IsAny<CreateOrderRequest>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void SensitiveEndpoints_RequireOwnerRole()
    {
        var ownerOnly = new (Type Type, string Method)[]
        {
            (typeof(ShopController), nameof(ShopController.Update)),
            (typeof(OrdersController), nameof(OrdersController.Cancel)),
            (typeof(BillingController), nameof(BillingController.Pay)),
            (typeof(BillingController), nameof(BillingController.ChangePlan)),
            (typeof(StaffController), nameof(StaffController.Create)),
            (typeof(StaffController), nameof(StaffController.Update)),
            (typeof(StaffController), nameof(StaffController.Deactivate))
        };

        foreach (var (type, method) in ownerOnly)
        {
            var info = type.GetMethod(method);
            Assert.NotNull(info);
            Assert.Equal("Owner", info!.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        }
    }

    [Fact]
    public async Task AdminStats_RequiresAdminClaim()
    {
        var service = new Mock<IAdminService>();
        service.Setup(s => s.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminStatsDto { ShopCount = 1 });
        var controller = new AdminController(service.Object);

        Authenticate(controller, Guid.NewGuid());
        Assert.IsType<ForbidResult>(await controller.Stats());

        Authenticate(controller, Guid.NewGuid(), isAdmin: true);
        Assert.IsType<OkObjectResult>(await controller.Stats());
    }
}

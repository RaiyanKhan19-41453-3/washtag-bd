using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Washtag.Api.Controllers;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Xunit;

namespace Washtag.Api.Tests;

public class ControllerTests
{
    private static void Authenticate(ControllerBase controller, Guid shopId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("shopId", shopId.ToString()),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Owner"),
            new Claim(ClaimTypes.Role, "Owner")
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
        var controller = new OrdersController(service.Object);
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
        service.Setup(s => s.AskAsync(shopId, "baki taka koto?", It.IsAny<CancellationToken>()))
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
        service.Verify(s => s.AskAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public void SensitiveEndpoints_RequireOwnerRole()
    {
        var shopUpdate = typeof(ShopController).GetMethod(nameof(ShopController.Update));
        var cancel = typeof(OrdersController).GetMethod(nameof(OrdersController.Cancel));

        Assert.NotNull(shopUpdate);
        Assert.NotNull(cancel);
        Assert.Equal("Owner", shopUpdate!.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        Assert.Equal("Owner", cancel!.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }
}

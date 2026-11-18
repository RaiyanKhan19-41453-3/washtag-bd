using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class DashboardController : ControllerBase
{
    private readonly IOrderService _orders;

    public DashboardController(IOrderService orders) => _orders = orders;

    private Guid ShopId => Guid.Parse(User.FindFirstValue("shopId")!);

    [HttpGet("dashboard")]
    public async Task<IActionResult> Get() => Ok(await _orders.GetDashboardAsync(ShopId));

    [HttpGet("customers")]
    public async Task<IActionResult> Customers() => Ok(await _orders.GetCustomersAsync(ShopId));
}

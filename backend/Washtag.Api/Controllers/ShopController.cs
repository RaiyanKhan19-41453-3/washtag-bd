using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/shop")]
public class ShopController : ControllerBase
{
    private readonly IOrderService _orders;
    private readonly IAuthService _auth;

    public ShopController(IOrderService orders, IAuthService auth)
    {
        _orders = orders;
        _auth = auth;
    }

    private Guid ShopId => Guid.Parse(User.FindFirstValue("shopId")!);
    private Guid UserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var profile = await _auth.GetProfileAsync(UserId);
        return Ok(profile.Shop);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateShopRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _orders.UpdateShopAsync(ShopId, request));
    }
}

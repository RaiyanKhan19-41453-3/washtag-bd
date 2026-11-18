using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public")]
public class PublicController : ControllerBase
{
    private readonly IOrderService _orders;

    public PublicController(IOrderService orders) => _orders = orders;

    [HttpGet("track/{token}")]
    public async Task<IActionResult> Track(string token)
    {
        var result = await _orders.TrackAsync(token);
        return result == null
            ? NotFound(new { message = "No order found for this QR code." })
            : Ok(result);
    }
}

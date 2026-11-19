using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin) => _admin = admin;

    private bool IsPlatformAdmin => User.FindFirst("isAdmin")?.Value == "true";

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!IsPlatformAdmin) return Forbid();
        return Ok(await _admin.GetStatsAsync());
    }

    [HttpPatch("shops/{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] AdminShopActiveRequest request)
    {
        if (!IsPlatformAdmin) return Forbid();
        return Ok(await _admin.SetShopActiveAsync(id, request.Active, HttpContext.RequestAborted));
    }

    [HttpPatch("shops/{id:guid}/cancel")]
    public async Task<IActionResult> CancelSubscription(Guid id)
    {
        if (!IsPlatformAdmin) return Forbid();
        return Ok(await _admin.CancelShopSubscriptionAsync(id, HttpContext.RequestAborted));
    }
}

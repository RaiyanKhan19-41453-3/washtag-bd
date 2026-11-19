using System.Security.Claims;
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
    private readonly IAdminSettings _settings;

    public AdminController(IAdminService admin, IAdminSettings settings)
    {
        _admin = admin;
        _settings = settings;
    }

    // Re-check the live configuration as well as the signed claim so removing an
    // address from ADMIN_EMAILS takes effect immediately instead of after token expiry.
    private bool IsPlatformAdmin =>
        User.FindFirst("isAdmin")?.Value == "true" &&
        User.FindFirst(ClaimTypes.Email)?.Value is { } email && _settings.IsAdmin(email);

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

    [HttpPatch("shops/{id:guid}/restore")]
    public async Task<IActionResult> RestoreSubscription(Guid id)
    {
        if (!IsPlatformAdmin) return Forbid();
        return Ok(await _admin.RestoreSubscriptionAsync(id, HttpContext.RequestAborted));
    }
}

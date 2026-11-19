using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/maintenance")]
public class MaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _maintenance;
    private readonly IAdminSettings _settings;

    public MaintenanceController(IMaintenanceService maintenance, IAdminSettings settings)
    {
        _maintenance = maintenance;
        _settings = settings;
    }

    private bool IsPlatformAdmin =>
        User.FindFirst("isAdmin")?.Value == "true" &&
        User.FindFirst(ClaimTypes.Email)?.Value is { } email && _settings.IsAdmin(email);

    [HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        if (!IsPlatformAdmin) return Forbid();
        return Ok(await _maintenance.RunAsync(HttpContext.RequestAborted));
    }
}

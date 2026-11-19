using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Washtag.Application;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/maintenance")]
public class MaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _maintenance;
    private readonly IAdminSettings _settings;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceController> _logger;

    public MaintenanceController(IMaintenanceService maintenance, IAdminSettings settings,
        MaintenanceOptions options, ILogger<MaintenanceController> logger)
    {
        _maintenance = maintenance;
        _settings = settings;
        _options = options;
        _logger = logger;
    }

    private bool IsPlatformAdmin =>
        User.FindFirst("isAdmin")?.Value == "true" &&
        User.FindFirst(ClaimTypes.Email)?.Value is { } email && _settings.IsAdmin(email);

    [HttpPost("run")]
    [EnableRateLimiting("maintenance")]
    public async Task<IActionResult> Run()
    {
        if (!IsPlatformAdmin) return Forbid();
        if (!_options.Enabled)
            return StatusCode(503, new { message = "Maintenance is disabled in configuration." });

        _logger.LogWarning("Manual maintenance run by {Admin}",
            User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown");
        return Ok(await _maintenance.RunAsync(HttpContext.RequestAborted));
    }
}

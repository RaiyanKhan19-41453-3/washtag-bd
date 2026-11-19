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

    public MaintenanceController(IMaintenanceService maintenance) => _maintenance = maintenance;

    private bool IsPlatformAdmin => User.FindFirst("isAdmin")?.Value == "true";

    [HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        if (!IsPlatformAdmin) return Forbid();
        return Ok(await _maintenance.RunAsync(HttpContext.RequestAborted));
    }
}

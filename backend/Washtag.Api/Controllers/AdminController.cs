using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}

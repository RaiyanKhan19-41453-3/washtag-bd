using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    public ReportsController(IReportService reports) => _reports = reports;

    private Guid ShopId => Guid.TryParse(User.FindFirst("shopId")?.Value, out var id) ? id : Guid.Empty;

    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (to <= from) return BadRequest(new { message = "The end date must be after the start date." });
        return Ok(await _reports.DailyAsync(ShopId, from, to, HttpContext.RequestAborted));
    }

    [HttpGet("orders.csv")]
    public async Task<IActionResult> OrdersCsv([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (to <= from) return BadRequest(new { message = "The end date must be after the start date." });
        var bytes = await _reports.OrdersCsvAsync(ShopId, from, to, HttpContext.RequestAborted);
        return File(bytes, "text/csv; charset=utf-8", $"orders-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }
}

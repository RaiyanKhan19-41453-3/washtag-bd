using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/assistant")]
public class AssistantController : ControllerBase
{
    private readonly IAssistantService _assistant;

    public AssistantController(IAssistantService assistant) => _assistant = assistant;

    private Guid ShopId => Guid.Parse(User.FindFirstValue("shopId")!);
    private Guid UserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }

    [EnableRateLimiting("ai")]
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AssistantAskRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Ask a question first." });

        return Ok(await _assistant.AskAsync(ShopId, UserId, request.Question));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int limit = 50) =>
        Ok(await _assistant.GetHistoryAsync(ShopId, UserId, limit));

    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory()
    {
        await _assistant.ClearHistoryAsync(ShopId, UserId);
        return NoContent();
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] AssistantFeedbackRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        await _assistant.SubmitFeedbackAsync(ShopId, UserId, request);
        return Ok(new { message = "Thanks for the feedback." });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await _assistant.GetStatusAsync(ShopId));

    [EnableRateLimiting("ai")]
    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex() => Ok(new { documents = await _assistant.ReindexAsync(ShopId) });
}

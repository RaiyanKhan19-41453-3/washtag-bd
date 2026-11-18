using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AssistantAskRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Ask a question first." });

        return Ok(await _assistant.AskAsync(ShopId, request.Question));
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await _assistant.GetStatusAsync(ShopId));

    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex() => Ok(new { documents = await _assistant.ReindexAsync(ShopId) });
}

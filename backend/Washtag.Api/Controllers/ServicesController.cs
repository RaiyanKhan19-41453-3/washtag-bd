using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/services")]
public class ServicesController : ControllerBase
{
    private readonly IServiceCatalogService _services;
    public ServicesController(IServiceCatalogService services) => _services = services;

    private Guid ShopId => Guid.TryParse(User.FindFirst("shopId")?.Value, out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _services.ListAsync(ShopId, HttpContext.RequestAborted));

    [Authorize(Roles = "Owner")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _services.CreateAsync(ShopId, request, HttpContext.RequestAborted));
    }

    [Authorize(Roles = "Owner")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _services.UpdateAsync(ShopId, id, request, HttpContext.RequestAborted));
    }

    [Authorize(Roles = "Owner")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _services.DeleteAsync(ShopId, id, HttpContext.RequestAborted);
        return NoContent();
    }
}

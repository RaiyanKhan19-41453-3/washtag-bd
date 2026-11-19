using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;

namespace Washtag.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("webhook")]
[RequestSizeLimit(8192)]
[Route("api/public/payments")]
public class PaymentWebhookController : ControllerBase
{
    private readonly IInvoiceStore _invoices;
    private readonly IAppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(IInvoiceStore invoices, IAppDbContext db, IConfiguration configuration,
        ILogger<PaymentWebhookController> logger)
    {
        _invoices = invoices;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var secret = _configuration["PAYMENT_WEBHOOK_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503, new { message = "Payment webhook is not configured." });

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var expected = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        var provided = Request.Headers["X-WashTag-Signature"].FirstOrDefault() ?? string.Empty;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            return Unauthorized(new { message = "Invalid webhook signature." });

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return BadRequest(new { message = "Invalid payload." }); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return BadRequest(new { message = "Invalid payload." });
            var evt = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (evt != "invoice.paid") return Ok(new { accepted = false, reason = "unsupported event" });
            if (!Guid.TryParse(root.TryGetProperty("shopId", out var s) ? s.GetString() : null, out var shopId) ||
                !Guid.TryParse(root.TryGetProperty("invoiceId", out var i) ? i.GetString() : null, out var invoiceId))
                return BadRequest(new { message = "shopId and invoiceId are required." });

            var invoice = await _db.Invoices.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == invoiceId && x.ShopId == shopId, cancellationToken);
            if (invoice == null) return Ok(new { accepted = false, reason = "invoice not found" });
            if (invoice.Status == InvoiceStatus.Paid) return Ok(new { accepted = true }); // gateway retry

            if (!root.TryGetProperty("amount", out var amountNode) ||
                amountNode.ValueKind != JsonValueKind.Number ||
                !amountNode.TryGetDecimal(out var amount) || amount != invoice.Amount)
                return BadRequest(new { message = "Payment amount does not match the invoice." });

            var method = PaymentMethod.Cash;
            if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String &&
                Enum.TryParse<PaymentMethod>(m.GetString(), true, out var parsed) && Enum.IsDefined(parsed))
                method = parsed;
            var reference = root.TryGetProperty("reference", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() : null;
            if (reference?.Length > 100) return BadRequest(new { message = "Reference is too long." });

            var settled = await _invoices.SettleAsync(shopId, invoiceId, method, reference, cancellationToken);
            if (!settled) return Ok(new { accepted = false, reason = "invoice not payable" });
            _logger.LogInformation("Verified payment for shop {ShopId} invoice {InvoiceId}", shopId, invoiceId);
            return Ok(new { accepted = true });
        }
    }
}

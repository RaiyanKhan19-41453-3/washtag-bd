using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class BillingService : IBillingService
{
    private readonly IAppDbContext _db;
    private readonly IInvoiceStore _invoices;
    private readonly BillingOptions _options;

    public BillingService(IAppDbContext db, IInvoiceStore invoices, BillingOptions options)
    {
        _db = db;
        _invoices = invoices;
        _options = options;
    }

    public async Task<BillingOverviewDto> GetOverviewAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var shop = await _db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");

        // Older shops created before registration seeded invoices need a backfill,
        // otherwise there is nothing to pay and they can never renew after expiry.
        // A cancelled plan is terminal — don't mint an invoice the owner can never use.
        if (shop.SubscriptionStatus != SubscriptionStatus.Cancelled)
            await _invoices.EnsureCurrentInvoiceAsync(shopId, Plans.MonthlyPrice(shop.Plan), cancellationToken);

        // Apply billing deadlines atomically; a stale tracked read must never be able
        // to overwrite a payment that settled concurrently.
        await _invoices.ApplyDeadlinesAsync(DateTime.UtcNow, shopId, cancellationToken);
        shop = await _db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken) ?? shop;

        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.ShopId == shopId)
            .OrderByDescending(i => i.PeriodTo)
            .ToListAsync(cancellationToken);

        return new BillingOverviewDto
        {
            CanSelfSettle = _options.ManualPaymentEnabled,
            Plan = shop.Plan,
            Status = shop.SubscriptionStatus,
            StartedAt = shop.SubscriptionStartedAt,
            RenewalDate = shop.SubscriptionEndsAt,
            MonthlyPrice = Plans.MonthlyPrice(shop.Plan),
            Outstanding = invoices.Where(i => i.Status is InvoiceStatus.Pending or InvoiceStatus.Overdue)
                .Sum(i => i.Amount),
            Invoices = invoices.Select(Map).ToList()
        };
    }

    public async Task<InvoiceDto> MarkPaidAsync(Guid shopId, Guid invoiceId, MarkInvoicePaidRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.ShopId == shopId, cancellationToken)
            ?? throw new AppException("Invoice not found.");
        if (invoice.Status == InvoiceStatus.Paid)
            throw new AppException("This invoice is already paid.");
        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new AppException("This invoice was cancelled.");

        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        var settled = await _invoices.SettleAsync(shopId, invoiceId, request.Method, reference, cancellationToken);
        if (!settled)
            throw new AppException("The invoice state changed. Reload and try again.");

        var updated = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ShopId == shopId, cancellationToken)
            ?? throw new AppException("Invoice not found.");
        return Map(updated);
    }

    public async Task<BillingOverviewDto> ChangePlanAsync(Guid shopId, ChangePlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Plans.IsValid(request.Plan))
            throw new AppException("Choose one of: Starter, Growth or Pro.");

        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken)
            ?? throw new AppException("Shop not found.");
        var plan = PlanName(request.Plan);
        var price = Plans.MonthlyPrice(plan);

        shop.Plan = plan;
        var pending = await _db.Invoices
            .Where(i => i.ShopId == shopId && i.Status == InvoiceStatus.Pending && i.PeriodTo > DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var invoice in pending) invoice.Amount = price;

        await _db.SaveChangesAsync(cancellationToken);
        return await GetOverviewAsync(shopId, cancellationToken);
    }

    private static string PlanName(string value) => Plans.All.First(p =>
        p.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static InvoiceDto Map(Invoice invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        PeriodFrom = invoice.PeriodFrom,
        PeriodTo = invoice.PeriodTo,
        Amount = invoice.Amount,
        Status = invoice.Status,
        PaidAt = invoice.PaidAt,
        Method = invoice.Method,
        Reference = invoice.Reference,
        Notes = invoice.Notes
    };
}

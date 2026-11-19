using Microsoft.EntityFrameworkCore;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;

namespace Washtag.Infrastructure.Services;

/// <summary>Turns shop transactions, customers and order statuses into searchable vector documents.</summary>
public class DocumentIndexer : IDocumentIndexer
{
    private readonly IAppDbContext _db;
    private readonly IVectorStore _vectors;
    private readonly IAiProvider _ai;

    public DocumentIndexer(IAppDbContext db, IVectorStore vectors, IAiProvider ai)
    {
        _db = db;
        _vectors = vectors;
        _ai = ai;
    }

    public async Task IndexOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = BuildOrderDocument(order);
            var embedding = await _ai.EmbedAsync(content, cancellationToken);
            await _vectors.UpsertAsync(order.ShopId, "order", order.Id, content, embedding, cancellationToken);
        }
        catch { /* Indexing must never break a shop operation. */ }
    }

    public async Task IndexCustomerAsync(Guid shopId, string phone, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Phone == phone, cancellationToken);
            if (customer == null) return;

            var stats = await _db.Orders.AsNoTracking()
                .Where(o => o.ShopId == shopId && o.CustomerPhone == phone && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Total = g.Sum(o => o.TotalAmount),
                    Paid = g.Sum(o => o.PaidAmount)
                }).FirstOrDefaultAsync(cancellationToken);

            var content = $"Customer {customer.Name}, phone {customer.Phone}. " +
                          $"Has {stats?.Count ?? 0} order(s), total ৳{stats?.Total ?? 0:0}, " +
                          $"paid ৳{stats?.Paid ?? 0:0}, due ৳{(stats?.Total ?? 0) - (stats?.Paid ?? 0):0}.";
            var embedding = await _ai.EmbedAsync(content, cancellationToken);
            await _vectors.UpsertAsync(shopId, "customer", customer.Id, content, embedding, cancellationToken);
        }
        catch { /* Indexing must never break a shop operation. */ }
    }

    public async Task ReindexShopAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .Include(o => o.Items)
            .Include(o => o.Shop)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var order in orders) await IndexOrderAsync(order, cancellationToken);

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.ShopId == shopId).Take(500).ToListAsync(cancellationToken);
        foreach (var customer in customers)
            await IndexCustomerAsync(shopId, customer.Phone, cancellationToken);

        // Remove embeddings for records that no longer exist so sources never link to ghosts.
        try
        {
            var existing = await _vectors.GetRefsAsync(shopId, cancellationToken);
            var orderIds = orders.Select(o => o.Id).ToHashSet();
            var customerIds = customers.Select(c => c.Id).ToHashSet();
            foreach (var reference in existing)
            {
                var stale = reference.DocType switch
                {
                    "order" => !orderIds.Contains(reference.RefId),
                    "customer" => !customerIds.Contains(reference.RefId),
                    _ => true
                };
                if (stale)
                    await _vectors.DeleteAsync(shopId, reference.DocType, reference.RefId, cancellationToken);
            }
        }
        catch { /* pruning is best-effort */ }
    }

    private static string BuildOrderDocument(Order order)
    {
        var items = string.Join(", ", order.Items.Select(item =>
            $"{item.Quantity}x {item.Name} ({item.Service}) at ৳{item.UnitPrice} each"));
        var due = order.Status == OrderStatus.Cancelled ? 0 : order.TotalAmount - order.PaidAmount;
        return $"Order #{order.OrderNumber} for customer {order.CustomerName}, phone {order.CustomerPhone}. " +
               $"Status: {order.Status}. Payment status: {order.PaymentStatus}. " +
               $"Total ৳{order.TotalAmount:0}, paid ৳{order.PaidAmount:0}, due ৳{due:0}. " +
               $"Created {order.CreatedAt:yyyy-MM-dd}, expected pickup {order.DueDate:yyyy-MM-dd}, " +
               $"delivered {order.DeliveredAt:yyyy-MM-dd}. " +
               $"Items: {items}. Notes: {(order.Notes ?? "none")[..Math.Min(order.Notes?.Length ?? 4, 1000)]}.";
    }
}

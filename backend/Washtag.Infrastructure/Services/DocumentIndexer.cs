using Microsoft.EntityFrameworkCore;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;

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

    public async Task ReindexShopAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        try { await _vectors.EnsureSchemaAsync(cancellationToken); }
        catch { return; }

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .Include(o => o.Items)
            .Include(o => o.Shop)
            .ToListAsync(cancellationToken);
        foreach (var order in orders) await IndexOrderAsync(order, cancellationToken);

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.ShopId == shopId).ToListAsync(cancellationToken);
        var shopOrders = orders.Select(o => new { o.CustomerPhone, o.TotalAmount, o.PaidAmount }).ToList();

        foreach (var customer in customers)
        {
            try
            {
                var mine = shopOrders.Where(o => o.CustomerPhone == customer.Phone).ToList();
                var content = $"Customer {customer.Name}, phone {customer.Phone}. " +
                              $"Has {mine.Count} order(s), total ৳{mine.Sum(o => o.TotalAmount):0}, " +
                              $"paid ৳{mine.Sum(o => o.PaidAmount):0}, due ৳{mine.Sum(o => o.TotalAmount - o.PaidAmount):0}.";
                var embedding = await _ai.EmbedAsync(content, cancellationToken);
                await _vectors.UpsertAsync(shopId, "customer", customer.Id, content, embedding, cancellationToken);
            }
            catch { /* skip this document */ }
        }
    }

    private static string BuildOrderDocument(Order order)
    {
        var items = string.Join(", ", order.Items.Select(item =>
            $"{item.Quantity}x {item.Name} ({item.Service}) at ৳{item.UnitPrice} each"));
        var due = order.TotalAmount - order.PaidAmount;
        return $"Order #{order.OrderNumber} for customer {order.CustomerName}, phone {order.CustomerPhone}. " +
               $"Status: {order.Status}. Payment status: {order.PaymentStatus}. " +
               $"Total ৳{order.TotalAmount:0}, paid ৳{order.PaidAmount:0}, due ৳{due:0}. " +
               $"Created {order.CreatedAt:yyyy-MM-dd}, expected pickup {order.DueDate:yyyy-MM-dd}, " +
               $"delivered {order.DeliveredAt:yyyy-MM-dd}. " +
               $"Items: {items}. Notes: {order.Notes ?? "none"}.";
    }
}

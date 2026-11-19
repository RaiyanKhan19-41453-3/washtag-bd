using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;

namespace Washtag.Application.Services;

public sealed class ReportService : IReportService
{
    private readonly IAppDbContext _db;
    public ReportService(IAppDbContext db) => _db = db;

    public async Task<DailyReportDto> DailyAsync(Guid shopId, DateTime from, DateTime to,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId && o.CreatedAt >= from && o.CreatedAt < to &&
                        o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.Id, o.CreatedAt, o.TotalAmount, o.PaidAmount })
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => orderIds.Contains(p.OrderId) && p.CreatedAt >= from && p.CreatedAt < to)
            .Select(p => new { p.OrderId, p.Amount, p.Method, p.CreatedAt })
            .ToListAsync(cancellationToken);

        return new DailyReportDto
        {
            OrderCount = orders.Count,
            Collected = payments.Sum(p => p.Amount),
            Outstanding = orders.Sum(o => o.TotalAmount - o.PaidAmount),
            ByMethod = payments.GroupBy(p => p.Method)
                .Select(g => new PaymentMethodTotalDto { Method = g.Key, Amount = g.Sum(p => p.Amount) })
                .OrderByDescending(x => x.Amount).ToList(),
            ByDay = orders.GroupBy(o => o.CreatedAt.Date)
                .Select(g => new DayTotalDto
                {
                    Date = g.Key, Orders = g.Count(),
                    Collected = payments.Where(p => g.Select(o => o.Id).Contains(p.OrderId))
                        .Sum(p => p.Amount)
                }).OrderBy(d => d.Date).ToList()
        };
    }

    public async Task<byte[]> OrdersCsvAsync(Guid shopId, DateTime from, DateTime to,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId && o.CreatedAt >= from && o.CreatedAt < to)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderNumber, o.CustomerName, o.CustomerPhone, o.Status, o.PaymentStatus,
                o.TotalAmount, o.PaidAmount, o.CreatedAt, o.DueDate
            }).ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Order,Customer,Phone,Status,Payment,Total,Paid,Created,Due");
        foreach (var o in orders)
            sb.AppendLine(string.Join(',',
                Escape(o.OrderNumber), Escape(o.CustomerName), Escape(o.CustomerPhone),
                o.Status.ToString(), o.PaymentStatus.ToString(),
                o.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture),
                o.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture),
                o.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                o.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : $"\"{value.Replace("\"", "\"\"")}\"";
}

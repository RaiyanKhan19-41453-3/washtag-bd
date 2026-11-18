using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class OrderService : IOrderService
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly ICacheService _cache;
    private readonly IQrCodeService _qr;
    private readonly ITrackingUrlProvider _urls;
    private readonly IDocumentIndexer _indexer;
    private readonly IOrderNumberGenerator _numbers;

    public OrderService(
        IAppDbContext db,
        IRealtimeNotifier notifier,
        ICacheService cache,
        IQrCodeService qr,
        ITrackingUrlProvider urls,
        IDocumentIndexer indexer,
        IOrderNumberGenerator numbers)
    {
        _db = db;
        _notifier = notifier;
        _cache = cache;
        _qr = qr;
        _urls = urls;
        _indexer = indexer;
        _numbers = numbers;
    }

    public async Task<PagedResult<OrderListDto>> GetOrdersAsync(Guid shopId, string? status, string? search, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Orders.AsNoTracking().Where(o => o.ShopId == shopId).Include(o => o.Items).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
            query = query.Where(o => o.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(o =>
                o.CustomerName.ToLower().Contains(term) ||
                o.CustomerPhone.Contains(term) ||
                o.OrderNumber.ToLower().Contains(term));
        }

        var total = await query.CountAsync();
        var orders = await query.OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<OrderListDto>
        {
            Items = orders.Select(MapList).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<OrderDetailDto> GetOrderAsync(Guid shopId, Guid id)
    {
        var order = await LoadOrderAsync(q => q.Id == id && q.ShopId == shopId);
        return MapDetail(order ?? throw new AppException("Order not found."));
    }

    public async Task<OrderDetailDto> GetOrderByTokenAsync(Guid shopId, string token)
    {
        if (!Guid.TryParse(token, out var guid)) throw new AppException("Invalid QR token.");
        var order = await LoadOrderAsync(q => q.PublicToken == guid && q.ShopId == shopId);
        return MapDetail(order ?? throw new AppException("No order matches this QR code."));
    }

    public async Task<OrderDetailDto> CreateOrderAsync(Guid shopId, CreateOrderRequest request, string actor)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw new AppException("Add at least one item to the order.");
        if (string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.CustomerPhone))
            throw new AppException("Customer name and phone are required.");

        var orderNumber = await _numbers.NextAsync(shopId);
        var now = DateTime.UtcNow;
        var order = new Order
        {
            ShopId = shopId,
            OrderNumber = orderNumber,
            PublicToken = Guid.NewGuid(),
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            DueDate = request.DueDate,
            Status = OrderStatus.Received,
            PaymentStatus = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            if (item.Quantity <= 0) throw new AppException("Item quantity must be at least 1.");
            var line = new OrderItem
            {
                Name = item.Name.Trim(),
                Service = string.IsNullOrWhiteSpace(item.Service) ? "Wash & Fold" : item.Service.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim()
            };
            order.Items.Add(line);
            order.TotalAmount += line.Quantity * line.UnitPrice;
        }

        if (order.Items.Count == 0) throw new AppException("Add at least one valid item to the order.");

        order.StatusHistory.Add(new StatusEvent
        {
            FromStatus = null,
            ToStatus = OrderStatus.Received,
            Note = "Order received from customer",
            ActorName = actor,
            CreatedAt = now
        });

        var phone = order.CustomerPhone;
        if (!await _db.Customers.AnyAsync(c => c.ShopId == shopId && c.Phone == phone))
        {
            _db.Customers.Add(new Customer
            {
                ShopId = shopId,
                Name = order.CustomerName,
                Phone = phone,
                CreatedAt = now
            });
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var dto = MapDetail(order);
        await IndexAsync(order);
        await _notifier.OrderUpdatedAsync(order, dto, BuildPublicTrack(order));
        return dto;
    }

    public async Task<OrderDetailDto> UpdateStatusAsync(Guid shopId, Guid id, UpdateStatusRequest request, string actor)
    {
        var order = await LoadOrderAsync(q => q.Id == id && q.ShopId == shopId)
            ?? throw new AppException("Order not found.");

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new AppException("This order is already completed and cannot be changed.");
        if (request.Status == order.Status)
            throw new AppException("The order is already at this status.");
        if (request.Status == OrderStatus.Cancelled)
        {
            await CancelOrderAsync(shopId, id, actor);
            return await GetOrderAsync(shopId, id);
        }

        var previous = order.Status;
        order.Status = request.Status;
        order.UpdatedAt = DateTime.UtcNow;
        if (request.Status == OrderStatus.Delivered) order.DeliveredAt = DateTime.UtcNow;

        var entry = new StatusEvent
        {
            FromStatus = previous,
            ToStatus = request.Status,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ActorName = actor,
            CreatedAt = DateTime.UtcNow
        };
        order.StatusHistory.Add(entry);
        _db.StatusEvents.Add(entry);

        await _db.SaveChangesAsync();
        await InvalidateCacheAsync(order.PublicToken);
        await IndexAsync(order);
        await _notifier.OrderUpdatedAsync(order, MapDetail(order), BuildPublicTrack(order));
        return MapDetail(order);
    }

    public async Task<OrderDetailDto> RecordPaymentAsync(Guid shopId, Guid id, RecordPaymentRequest request, string actor)
    {
        var order = await LoadOrderAsync(q => q.Id == id && q.ShopId == shopId)
            ?? throw new AppException("Order not found.");
        if (request.Amount <= 0) throw new AppException("Payment amount must be greater than zero.");
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new AppException("Payments cannot be recorded on a completed order.");
        var balance = order.TotalAmount - order.PaidAmount;
        if (request.Amount > balance)
            throw new AppException($"Payment exceeds the outstanding balance of ৳{balance:0}.");

        order.PaidAmount += request.Amount;
        order.PaymentStatus = order.PaidAmount <= 0 ? PaymentStatus.Unpaid
            : order.PaidAmount >= order.TotalAmount ? PaymentStatus.Paid : PaymentStatus.Partial;
        order.UpdatedAt = DateTime.UtcNow;

        var record = new Payment
        {
            Amount = request.Amount,
            Method = request.Method,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            RecordedBy = actor,
            CreatedAt = DateTime.UtcNow
        };
        order.Payments.Add(record);
        _db.Payments.Add(record);

        await _db.SaveChangesAsync();
        await InvalidateCacheAsync(order.PublicToken);
        await IndexAsync(order);
        await _notifier.OrderUpdatedAsync(order, MapDetail(order), BuildPublicTrack(order));
        return MapDetail(order);
    }

    public async Task CancelOrderAsync(Guid shopId, Guid id, string actor)
    {
        var order = await LoadOrderAsync(q => q.Id == id && q.ShopId == shopId)
            ?? throw new AppException("Order not found.");
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new AppException("This order is already completed.");

        var previous = order.Status;
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        var entry = new StatusEvent
        {
            FromStatus = previous,
            ToStatus = OrderStatus.Cancelled,
            Note = "Order cancelled",
            ActorName = actor,
            CreatedAt = DateTime.UtcNow
        };
        order.StatusHistory.Add(entry);
        _db.StatusEvents.Add(entry);

        await _db.SaveChangesAsync();
        await InvalidateCacheAsync(order.PublicToken);
        await IndexAsync(order);
        await _notifier.OrderUpdatedAsync(order, MapDetail(order), BuildPublicTrack(order));
    }

    public async Task<PublicTrackDto?> TrackAsync(string token)
    {
        if (!Guid.TryParse(token, out var guid)) return null;

        var cacheKey = $"track:{guid}";
        var cached = await _cache.GetAsync<PublicTrackDto>(cacheKey);
        if (cached != null) return cached;

        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.PublicToken == guid);

        if (order == null) return null;

        var result = BuildPublicTrack(order);
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromSeconds(60));
        return result;
    }

    public async Task<byte[]> GetQrPngAsync(Guid shopId, Guid id)
    {
        var order = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.ShopId == shopId)
            ?? throw new AppException("Order not found.");
        return _qr.GeneratePng(_urls.Build(order.PublicToken));
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid shopId)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var customerCount = await _db.Customers.CountAsync(c => c.ShopId == shopId);
        var today = DhakaNow.Date;
        var activeStatuses = new[]
        {
            OrderStatus.Received, OrderStatus.Washing, OrderStatus.Ironing, OrderStatus.ReadyForPickup
        };

        var statusBreakdown = Enum.GetValues<OrderStatus>()
            .Select(s => new StatusCountDto { Status = s.ToString(), Count = orders.Count(o => o.Status == s) })
            .ToList();

        var weeklyTrend = new List<DayCountDto>();
        for (var i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            weeklyTrend.Add(new DayCountDto
            {
                Label = day.ToString("ddd"),
                Count = orders.Count(o => o.CreatedAt.AddHours(6).Date == day)
            });
        }

        return new DashboardDto
        {
            TotalOrders = orders.Count,
            ReceivedCount = orders.Count(o => o.Status == OrderStatus.Received),
            WashingCount = orders.Count(o => o.Status == OrderStatus.Washing),
            IroningCount = orders.Count(o => o.Status == OrderStatus.Ironing),
            ReadyCount = orders.Count(o => o.Status == OrderStatus.ReadyForPickup),
            DeliveredCount = orders.Count(o => o.Status == OrderStatus.Delivered),
            CancelledCount = orders.Count(o => o.Status == OrderStatus.Cancelled),
            ActiveCount = orders.Count(o => activeStatuses.Contains(o.Status)),
            UnpaidCount = orders.Count(o => o.PaymentStatus != PaymentStatus.Paid),
            Collected = orders.Sum(o => o.PaidAmount),
            Outstanding = orders.Sum(o => o.TotalAmount - o.PaidAmount),
            TodayOrders = orders.Count(o => o.CreatedAt.AddHours(6).Date == today),
            CustomerCount = customerCount,
            RecentOrders = orders.Take(6).Select(MapList).ToList(),
            StatusBreakdown = statusBreakdown,
            WeeklyTrend = weeklyTrend
        };
    }

    public async Task<List<CustomerDto>> GetCustomersAsync(Guid shopId)
    {
        var orders = await _db.Orders.AsNoTracking().Where(o => o.ShopId == shopId).ToListAsync();

        return orders.GroupBy(o => o.CustomerPhone.Trim())
            .Select(group =>
            {
                var latest = group.OrderByDescending(o => o.CreatedAt).First();
                return new CustomerDto
                {
                    Name = latest.CustomerName,
                    Phone = group.Key,
                    OrderCount = group.Count(),
                    TotalSpent = group.Sum(o => o.TotalAmount),
                    UnpaidAmount = group.Sum(o => o.TotalAmount - o.PaidAmount),
                    LastOrderAt = group.Max(o => o.CreatedAt)
                };
            })
            .OrderByDescending(c => c.LastOrderAt)
            .ToList();
    }

    public async Task<ShopDto> UpdateShopAsync(Guid shopId, UpdateShopRequest request)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId)
            ?? throw new AppException("Shop not found.");

        shop.Name = request.Name.Trim();
        shop.Address = request.Address?.Trim() ?? string.Empty;
        shop.City = request.City?.Trim() ?? string.Empty;
        shop.Phone = request.Phone?.Trim() ?? string.Empty;
        shop.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();

        await _db.SaveChangesAsync();
        return AuthService.MapShop(shop);
    }

    // Bangladesh is UTC+6 with no DST; avoids an ICU dependency for timezone IDs.
    private static DateTime DhakaNow => DateTime.UtcNow.AddHours(6);

    private async Task<Order?> LoadOrderAsync(Expression<Func<Order, bool>> predicate)
    {
        return await _db.Orders
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(predicate);
    }

    private async Task InvalidateCacheAsync(Guid token)
    {
        try { await _cache.RemoveAsync($"track:{token}"); }
        catch { /* cache is best-effort */ }
    }

    private async Task IndexAsync(Order order)
    {
        try { await _indexer.IndexOrderAsync(order); }
        catch { /* vector indexing is best-effort */ }
    }

    private static OrderListDto MapList(Order order)
    {
        var dto = new OrderListDto();
        FillBase(order, dto);
        return dto;
    }

    private OrderDetailDto MapDetail(Order order)
    {
        var url = _urls.Build(order.PublicToken);
        var dto = new OrderDetailDto
        {
            Notes = order.Notes,
            DeliveredAt = order.DeliveredAt,
            TrackingUrl = url,
            QrDataUrl = _qr.GenerateDataUrl(url),
            Items = order.Items.Select(MapItem).ToList(),
            StatusHistory = order.StatusHistory.OrderBy(x => x.CreatedAt).Select(MapEvent).ToList(),
            Payments = order.Payments.OrderByDescending(x => x.CreatedAt).Select(MapPayment).ToList()
        };
        FillBase(order, dto);
        return dto;
    }

    private static void FillBase(Order order, OrderListDto dto)
    {
        dto.Id = order.Id;
        dto.OrderNumber = order.OrderNumber;
        dto.PublicToken = order.PublicToken.ToString();
        dto.CustomerName = order.CustomerName;
        dto.CustomerPhone = order.CustomerPhone;
        dto.Status = order.Status;
        dto.PaymentStatus = order.PaymentStatus;
        dto.TotalAmount = order.TotalAmount;
        dto.PaidAmount = order.PaidAmount;
        dto.ItemCount = order.Items.Count;
        dto.DueDate = order.DueDate;
        dto.CreatedAt = order.CreatedAt;
        dto.UpdatedAt = order.UpdatedAt;
    }

    private static OrderItemDto MapItem(OrderItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Service = item.Service,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        LineTotal = item.Quantity * item.UnitPrice,
        Notes = item.Notes
    };

    private static StatusEventDto MapEvent(StatusEvent e) => new()
    {
        Id = e.Id,
        FromStatus = e.FromStatus,
        ToStatus = e.ToStatus,
        Note = e.Note,
        ActorName = e.ActorName,
        CreatedAt = e.CreatedAt
    };

    private static PaymentDto MapPayment(Payment p) => new()
    {
        Id = p.Id,
        Amount = p.Amount,
        Method = p.Method,
        Reference = p.Reference,
        RecordedBy = p.RecordedBy,
        CreatedAt = p.CreatedAt
    };

    private static PublicTrackDto BuildPublicTrack(Order order) => new()
    {
        Shop = new ShopBrandDto
        {
            Name = order.Shop?.Name ?? string.Empty,
            Address = order.Shop?.Address ?? string.Empty,
            City = order.Shop?.City ?? string.Empty,
            Phone = order.Shop?.Phone ?? string.Empty,
            LogoUrl = order.Shop?.LogoUrl
        },
        Order = new PublicOrderDto
        {
            Token = order.PublicToken.ToString(),
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            CreatedAt = order.CreatedAt,
            DueDate = order.DueDate,
            DeliveredAt = order.DeliveredAt,
            Items = order.Items.Select(MapItem).ToList()
        },
        Timeline = order.StatusHistory.OrderBy(x => x.CreatedAt).Select(MapEvent).ToList()
    };
}

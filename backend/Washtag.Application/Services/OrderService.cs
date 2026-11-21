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
    private readonly ICustomerStore _customers;
    private readonly IPaymentStore _payments;
    private readonly IMessageSender _messages;

    public OrderService(
        IAppDbContext db,
        IRealtimeNotifier notifier,
        ICacheService cache,
        IQrCodeService qr,
        ITrackingUrlProvider urls,
        IDocumentIndexer indexer,
        IOrderNumberGenerator numbers,
        ICustomerStore customers,
        IPaymentStore payments,
        IMessageSender messages)
    {
        _db = db;
        _notifier = notifier;
        _cache = cache;
        _qr = qr;
        _urls = urls;
        _indexer = indexer;
        _numbers = numbers;
        _customers = customers;
        _payments = payments;
        _messages = messages;
    }

    public async Task<PagedResult<OrderListDto>> GetOrdersAsync(Guid shopId, string? status, string? search, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Orders.AsNoTracking().Where(o => o.ShopId == shopId).Include(o => o.Items).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var parsedStatus) &&
            Enum.IsDefined(parsedStatus))
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
        // Guard against int overflow on extreme page numbers - without this the skip
        // wraps negative and silently returns the first page.
        var skip = page > int.MaxValue / pageSize ? int.MaxValue : (page - 1) * pageSize;
        var orders = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Skip(skip).Take(pageSize).ToListAsync();

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

    public async Task<OrderDetailDto> CreateOrderAsync(Guid shopId, CreateOrderRequest request, string actor,
        string? idempotencyKey = null)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw new AppException("Add at least one item to the order.");
        if (string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.CustomerPhone))
            throw new AppException("Customer name and phone are required.");
        if (!Enum.IsDefined(request.DeliveryMode))
            throw new AppException("Invalid delivery mode.");
        if (request.DeliveryMode == DeliveryMode.Delivery && string.IsNullOrWhiteSpace(request.DeliveryAddress))
            throw new AppException("A delivery address is required for delivery orders.");

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key != null)
        {
            var replay = await LoadOrderAsync(o => o.ShopId == shopId && o.IdempotencyKey == key);
            if (replay != null)
                return await FinishCreateAsync(replay, true);
        }

        var orderNumber = await _numbers.NextAsync(shopId);
        var now = DateTime.UtcNow;
        var order = new Order
        {
            ShopId = shopId,
            OrderNumber = orderNumber,
            PublicToken = Guid.NewGuid(),
            IdempotencyKey = key,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            DueDate = request.DueDate,
            DeliveryMode = request.DeliveryMode,
            DeliveryAddress = request.DeliveryMode == DeliveryMode.Delivery && !string.IsNullOrWhiteSpace(request.DeliveryAddress)
                ? request.DeliveryAddress.Trim() : null,
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
                UnitPrice = decimal.Round(item.UnitPrice, 2, MidpointRounding.AwayFromZero),
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

        _db.Orders.Add(order);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (key != null)
        {
            // A concurrent request with the same key won the unique-index race.
            var winner = await LoadOrderAsync(o => o.ShopId == shopId && o.IdempotencyKey == key);
            if (winner != null)
                return await FinishCreateAsync(winner, true);
            throw;
        }
        return await FinishCreateAsync(order, false);
    }

    /// <summary>Idempotent post-commit work; also runs on a replay in case the first
    /// request died after the insert but before these side effects.</summary>
    private async Task<OrderDetailDto> FinishCreateAsync(Order order, bool replayed)
    {
        await _customers.EnsureAsync(order.ShopId, order.CustomerName, order.CustomerPhone, order.CreatedAt);
        var dto = MapDetail(order);
        dto.Replayed = replayed;
        await IndexAsync(order);
        // A replay must not re-broadcast an event for the same logical order.
        if (!replayed)
        {
            await NotifyAsync(order, MapRealtime(order), BuildPublicTrack(order));
            if (_messages.IsConfigured)
            {
                var shopContact = await _db.Shops.AsNoTracking()
                    .Where(s => s.Id == order.ShopId)
                    .Select(s => new { s.Phone, s.Email })
                    .FirstOrDefaultAsync();
                var to = shopContact?.Phone ?? shopContact?.Email;
                if (!string.IsNullOrWhiteSpace(to))
                    await _messages.SendAsync(to,
                        $"New order {order.OrderNumber} from {order.CustomerName} (৳{order.TotalAmount:0}). Track: {_urls.Build(order.PublicToken)}");
            }
        }
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
        if (request.Status != OrderStatus.Cancelled && (int)request.Status <= (int)order.Status)
            throw new AppException("Orders can only move forward in the workflow.");
        if (request.Status == OrderStatus.Delivered && order.PaidAmount < order.TotalAmount)
            throw new AppException("Settle the remaining balance before marking this order delivered.");
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
        await NotifyAsync(order, MapRealtime(order), BuildPublicTrack(order));
        if (_messages.IsConfigured && request.Status is OrderStatus.ReadyForPickup or OrderStatus.Delivered)
        {
            var message = request.Status == OrderStatus.ReadyForPickup
                ? order.DeliveryMode == DeliveryMode.Delivery
                    ? $"Order {order.OrderNumber} is out for delivery. Track: {_urls.Build(order.PublicToken)}"
                    : $"Order {order.OrderNumber} is ready for pickup. Track: {_urls.Build(order.PublicToken)}"
                : $"Order {order.OrderNumber} has been delivered. Thank you!";
            await _messages.SendAsync(order.CustomerPhone, message);
        }
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

        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        var recorded = await _payments.RecordAsync(shopId, id, request.Amount, request.Method, reference, actor);
        if (!recorded)
            throw new AppException("The order balance changed. Reload the order and try again.");

        // The payment was applied by raw SQL, so a tracked entity would still show the
        // pre-payment amounts. Reload without tracking to get the real balance.
        var fresh = await LoadOrderNoTrackingAsync(q => q.Id == id && q.ShopId == shopId)
            ?? throw new AppException("Order not found.");
        await InvalidateCacheAsync(order.PublicToken);
        await IndexAsync(fresh);
        await NotifyAsync(fresh, MapRealtime(fresh), BuildPublicTrack(fresh));
        return MapDetail(fresh);
    }

    public async Task CancelOrderAsync(Guid shopId, Guid id, string actor)
    {
        var order = await LoadOrderAsync(q => q.Id == id && q.ShopId == shopId)
            ?? throw new AppException("Order not found.");
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new AppException("This order is already completed.");

        var previous = order.Status;
        var refunded = order.PaidAmount;
        order.Status = OrderStatus.Cancelled;
        order.PaidAmount = 0;
        order.PaymentStatus = PaymentStatus.Unpaid;
        order.UpdatedAt = DateTime.UtcNow;
        var entry = new StatusEvent
        {
            FromStatus = previous,
            ToStatus = OrderStatus.Cancelled,
            Note = refunded > 0 ? $"Order cancelled; ৳{refunded:0} refunded" : "Order cancelled",
            ActorName = actor,
            CreatedAt = DateTime.UtcNow
        };
        order.StatusHistory.Add(entry);
        _db.StatusEvents.Add(entry);
        // Keep the audit trail consistent with PaidAmount = 0 after a refund.
        if (refunded > 0)
            _db.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Amount = -refunded,
                Method = PaymentMethod.Cash,
                Reference = "Refund on cancellation",
                RecordedBy = actor,
                CreatedAt = DateTime.UtcNow
            });

        await _db.SaveChangesAsync();
        await InvalidateCacheAsync(order.PublicToken);
        await IndexAsync(order);
        await NotifyAsync(order, MapRealtime(order), BuildPublicTrack(order));
    }

    public async Task<PublicTrackDto?> TrackAsync(string token)
    {
        if (!Guid.TryParse(token, out var guid)) return null;

        var cacheKey = $"track:{guid}";
        var cached = await _cache.GetAsync<PublicTrackDto>(cacheKey);
        if (cached != null) return cached;
        // Short negative cache stops random-GUID scans from hammering Postgres.
        if (await _cache.GetAsync<string>($"track-missing:{guid}") == "1") return null;

        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.PublicToken == guid);

        if (order == null)
        {
            await _cache.SetAsync($"track-missing:{guid}", "1", TimeSpan.FromSeconds(5));
            return null;
        }

        var result = BuildPublicTrack(order);
        // A mutation may have invalidated this key while we were reading. Never
        // write a stale snapshot back over the fresh state for the full TTL.
        var current = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == order.Id)
            .Select(o => new { o.UpdatedAt, o.Status, o.PaidAmount })
            .FirstOrDefaultAsync();
        if (current != null && current.UpdatedAt == order.UpdatedAt &&
            current.Status == order.Status && current.PaidAmount == order.PaidAmount)
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
        // Aggregate in SQL: the old version loaded every order (and its items) on
        // every dashboard poll, so cost grew with the whole history and client count.
        var today = DhakaNow.Date;
        var dayStartUtc = today.AddHours(-6);            // Dhaka midnight in UTC
        var tomorrowStartUtc = dayStartUtc.AddDays(1);
        var weekStartUtc = dayStartUtc.AddDays(-6);

        var stats = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .GroupBy(o => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Received = g.Count(o => o.Status == OrderStatus.Received),
                Washing = g.Count(o => o.Status == OrderStatus.Washing),
                Ironing = g.Count(o => o.Status == OrderStatus.Ironing),
                Ready = g.Count(o => o.Status == OrderStatus.ReadyForPickup),
                Delivered = g.Count(o => o.Status == OrderStatus.Delivered),
                Cancelled = g.Count(o => o.Status == OrderStatus.Cancelled),
                Active = g.Count(o => o.Status == OrderStatus.Received || o.Status == OrderStatus.Washing ||
                                      o.Status == OrderStatus.Ironing || o.Status == OrderStatus.ReadyForPickup),
                Unpaid = g.Count(o => o.PaymentStatus != PaymentStatus.Paid && o.Status != OrderStatus.Cancelled),
                Collected = g.Sum(o => o.PaidAmount),
                Outstanding = g.Sum(o => o.Status != OrderStatus.Cancelled ? o.TotalAmount - o.PaidAmount : 0),
                Today = g.Count(o => o.CreatedAt >= dayStartUtc && o.CreatedAt < tomorrowStartUtc)
            }).FirstOrDefaultAsync();

        var customerCount = await _db.Customers.CountAsync(c => c.ShopId == shopId);
        var recent = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(6).ToListAsync();

        var weekDates = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId && o.CreatedAt >= weekStartUtc && o.CreatedAt < tomorrowStartUtc)
            .Select(o => o.CreatedAt).ToListAsync();
        var weeklyTrend = Enumerable.Range(0, 7).Select(i =>
        {
            var day = today.AddDays(i - 6);
            return new DayCountDto
            {
                Label = day.ToString("ddd"),
                Count = weekDates.Count(d => d.AddHours(6).Date == day)
            };
        }).ToList();

        var breakdown = new Dictionary<OrderStatus, int>
        {
            [OrderStatus.Received] = stats?.Received ?? 0,
            [OrderStatus.Washing] = stats?.Washing ?? 0,
            [OrderStatus.Ironing] = stats?.Ironing ?? 0,
            [OrderStatus.ReadyForPickup] = stats?.Ready ?? 0,
            [OrderStatus.Delivered] = stats?.Delivered ?? 0,
            [OrderStatus.Cancelled] = stats?.Cancelled ?? 0
        };

        return new DashboardDto
        {
            TotalOrders = stats?.Total ?? 0,
            ReceivedCount = stats?.Received ?? 0,
            WashingCount = stats?.Washing ?? 0,
            IroningCount = stats?.Ironing ?? 0,
            ReadyCount = stats?.Ready ?? 0,
            DeliveredCount = stats?.Delivered ?? 0,
            CancelledCount = stats?.Cancelled ?? 0,
            ActiveCount = stats?.Active ?? 0,
            UnpaidCount = stats?.Unpaid ?? 0,
            Collected = stats?.Collected ?? 0,
            Outstanding = stats?.Outstanding ?? 0,
            TodayOrders = stats?.Today ?? 0,
            CustomerCount = customerCount,
            RecentOrders = recent.Select(MapList).ToList(),
            StatusBreakdown = breakdown.Select(x => new StatusCountDto { Status = x.Key.ToString(), Count = x.Value }).ToList(),
            WeeklyTrend = weeklyTrend
        };
    }

    public async Task<List<CustomerDto>> GetCustomersAsync(Guid shopId)
    {
        // Group and aggregate in the database instead of loading every order.
        return await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId)
            .GroupBy(o => o.CustomerPhone.Trim())
            .Select(group => new CustomerDto
            {
                Name = group.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
                    .Select(o => o.CustomerName).First(),
                Phone = group.Key,
                OrderCount = group.Count(o => o.Status != OrderStatus.Cancelled),
                TotalSpent = group.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                UnpaidAmount = group.Sum(o => o.Status != OrderStatus.Cancelled ? o.TotalAmount - o.PaidAmount : 0),
                LastOrderAt = group.Max(o => o.CreatedAt)
            })
            .OrderByDescending(c => c.LastOrderAt)
            .ToListAsync();
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
        var tokens = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == shopId).Select(o => o.PublicToken).ToListAsync();
        foreach (var token in tokens)
        {
            try { await _cache.RemoveAsync($"track:{token}"); }
            catch { /* cache is best-effort */ }
        }
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

    private async Task<Order?> LoadOrderNoTrackingAsync(Expression<Func<Order, bool>> predicate)
    {
        return await _db.Orders.AsNoTracking()
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
        // Incremental: only the changed order and its customer need re-embedding.
        try
        {
            await _indexer.IndexOrderAsync(order);
            await _indexer.IndexCustomerAsync(order.ShopId, order.CustomerPhone);
        }
        catch { /* vector indexing is best-effort */ }
    }

    private async Task NotifyAsync(Order order, object ownerPayload, object customerPayload)
    {
        try { await _notifier.OrderUpdatedAsync(order, ownerPayload, customerPayload); }
        catch { /* realtime push is best-effort */ }
    }

    private static OrderListDto MapList(Order order)
    {
        var dto = new OrderListDto();
        FillBase(order, dto);
        return dto;
    }

    // Realtime clients only need a refresh signal plus key fields; sending the full
    // detail DTO (with a base64 QR image) to every dashboard is wasteful.
    private static object MapRealtime(Order order) => new
    {
        order.Id,
        order.OrderNumber,
        order.Status,
        order.PaymentStatus,
        order.PaidAmount,
        order.TotalAmount,
        order.UpdatedAt
    };

    private OrderDetailDto MapDetail(Order order)
    {
        var url = _urls.Build(order.PublicToken);
        var dto = new OrderDetailDto
        {
            Notes = order.Notes,
            DeliveryAddress = order.DeliveryAddress,
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
        dto.DeliveryMode = order.DeliveryMode;
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

    private PublicTrackDto BuildPublicTrack(Order order) => new()
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
            DeliveryMode = order.DeliveryMode,
            DeliveryAddress = order.DeliveryAddress,
            TrackingUrl = _urls.Build(order.PublicToken),
            Items = order.Items.Select(MapItem).ToList()
        },
        Timeline = order.StatusHistory.OrderBy(x => x.CreatedAt).Select(MapEvent).ToList()
    };
}

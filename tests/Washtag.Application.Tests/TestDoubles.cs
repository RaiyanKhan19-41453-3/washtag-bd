using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;

namespace Washtag.Application.Tests;

/// <summary>In-memory IAppDbContext used by the use-case tests.</summary>
public sealed class TestDbContext : DbContext, IAppDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<StatusEvent> StatusEvents => Set<StatusEvent>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Order>().HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
        mb.Entity<Order>().HasMany(o => o.StatusHistory).WithOne().HasForeignKey(i => i.OrderId);
        mb.Entity<Order>().HasMany(o => o.Payments).WithOne().HasForeignKey(i => i.OrderId);
    }
}

public sealed class FakeNotifier : IRealtimeNotifier
{
    public int Calls { get; private set; }
    public Task OrderUpdatedAsync(Order order, object ownerPayload, object customerPayload)
    {
        Calls++;
        return Task.CompletedTask;
    }
}

public sealed class FakeCache : ICacheService
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    public List<string> Reads { get; } = new();
    public List<string> Writes { get; } = new();
    public List<string> Removes { get; } = new();

    public Task<T?> GetAsync<T>(string key)
    {
        Reads.Add(key);
        return Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed ? typed : default);
    }
    public Task SetAsync<T>(string key, T value, TimeSpan expiration)
    {
        Writes.Add(key);
        _values[key] = value;
        return Task.CompletedTask;
    }
    public Task RemoveAsync(string key)
    {
        Removes.Add(key);
        _values.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class FakeQr : IQrCodeService
{
    public string GenerateDataUrl(string payload) => "data:image/png;base64,AAAA";
    public byte[] GeneratePng(string payload) => new byte[] { 1, 2, 3 };
}

public sealed class FakeUrls : ITrackingUrlProvider
{
    public string Build(Guid token) => $"https://shop.test/track/{token}";
}

public sealed class FakeIndexer : IDocumentIndexer
{
    public int IndexCalls { get; private set; }
    public int ReindexCalls { get; private set; }
    public int CustomerIndexCalls { get; private set; }
    public Task IndexOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        IndexCalls++;
        return Task.CompletedTask;
    }
    public Task IndexCustomerAsync(Guid shopId, string phone, CancellationToken cancellationToken = default)
    {
        CustomerIndexCalls++;
        return Task.CompletedTask;
    }
    public Task ReindexShopAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        ReindexCalls++;
        return Task.CompletedTask;
    }
}

public sealed class FakeNumbers : IOrderNumberGenerator
{
    private int _current;
    public Task<string> NextAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        Task.FromResult($"WT-{Interlocked.Increment(ref _current):D5}");
}

public sealed class FakeAi : IAiProvider
{
    public string Name => "Test provider";
    public string EmbeddingModel => "test-384";
    public bool UsedLocalEmbedding => false;
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(new float[AiConstants.EmbeddingDimensions]);
    public Task<string?> GenerateAnswerAsync(string question, string groundedContext, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

public sealed class FakeVectors : IVectorStore
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpsertAsync(Guid shopId, string docType, Guid refId, string content, float[] embedding,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<List<DocumentMatch>> SearchAsync(Guid shopId, float[] embedding, int limit,
        CancellationToken cancellationToken = default) => Task.FromResult(new List<DocumentMatch>());
    public Task<int> CountAsync(Guid shopId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<List<DocumentRef>> GetRefsAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<DocumentRef>());
    public Task DeleteAsync(Guid shopId, string docType, Guid refId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class FakeTokens : IJwtTokenGenerator
{
    public string Generate(Washtag.Domain.Entities.AppUser user, Shop shop) => "signed-token";
}

public sealed class FakeCustomerStore : ICustomerStore
{
    public Task EnsureAsync(Guid shopId, string name, string phone, DateTime createdAt,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    private sealed record Entry(Guid UserId, bool Revoked, string? ReplacedBy, DateTime? RevokedAt = null);
    private readonly Dictionary<string, Entry> _tokens = new(StringComparer.Ordinal);

    private static string Hash(string raw) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    public Task<Guid?> TryClaimAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tokens.TryGetValue(tokenHash, out var e) && !e.Revoked ? e.UserId : (Guid?)null);

    public Task SetReplacedByAsync(string oldHash, string newHash, CancellationToken cancellationToken = default)
    {
        if (_tokens.TryGetValue(oldHash, out var e)) _tokens[oldHash] = e with { ReplacedBy = newHash };
        return Task.CompletedTask;
    }

    public int PurgeCalls { get; private set; }
    public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        PurgeCalls++;
        return Task.CompletedTask;
    }

    public Task<Guid?> RotateAsync(string oldHash, string newHash, DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (!_tokens.TryGetValue(oldHash, out var old))
            return Task.FromResult<Guid?>(null);
        if (old.Revoked)
        {
            // A just-rotated token is a concurrent refresh; leave the family alive.
            if (old.RevokedAt > DateTime.UtcNow.AddSeconds(-30))
                return Task.FromResult<Guid?>(null);
            // An old revoked token is replay theft: revoke the whole lineage.
            var cursor = old.ReplacedBy;
            while (cursor != null && _tokens.TryGetValue(cursor, out var next))
            {
                _tokens[cursor] = next with { Revoked = true, RevokedAt = DateTime.UtcNow };
                cursor = next.ReplacedBy;
            }
            return Task.FromResult<Guid?>(null);
        }
        _tokens[oldHash] = old with { Revoked = true, ReplacedBy = newHash, RevokedAt = DateTime.UtcNow };
        _tokens[newHash] = new Entry(old.UserId, false, null);
        return Task.FromResult<Guid?>(old.UserId);
    }

    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        foreach (var key in _tokens.Keys.ToList())
            if (_tokens[key].UserId == userId) _tokens[key] = _tokens[key] with { Revoked = true };
        return Task.CompletedTask;
    }

    public void Seed(string rawToken, Guid userId) => _tokens[Hash(rawToken)] = new Entry(userId, false, null);
    public void ExpireRotationGrace(string rawToken)
    {
        var hash = Hash(rawToken);
        if (_tokens.TryGetValue(hash, out var e))
            _tokens[hash] = e with { RevokedAt = DateTime.UtcNow.AddMinutes(-1) };
    }
    public bool IsRevoked(string rawToken) => _tokens.TryGetValue(Hash(rawToken), out var e) && e.Revoked;
    public string? ReplacedBy(string rawToken) =>
        _tokens.TryGetValue(Hash(rawToken), out var e) ? e.ReplacedBy : null;
}

public sealed class FakePaymentStore : IPaymentStore
{
    private readonly TestDbContext _db;
    public FakePaymentStore(TestDbContext db) => _db = db;

    public async Task<bool> RecordAsync(Guid shopId, Guid orderId, decimal amount, PaymentMethod method,
        string? reference, string recordedBy, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order == null || order.ShopId != shopId ||
            order.Status is OrderStatus.Delivered or OrderStatus.Cancelled ||
            order.TotalAmount - order.PaidAmount < amount) return false;

        order.PaidAmount += amount;
        order.PaymentStatus = order.PaidAmount >= order.TotalAmount ? PaymentStatus.Paid : PaymentStatus.Partial;
        order.UpdatedAt = DateTime.UtcNow;
        _db.Payments.Add(new Payment
        {
            OrderId = orderId, Amount = amount, Method = method, Reference = reference,
            RecordedBy = recordedBy, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class FakeInvoiceStore : IInvoiceStore
{
    private readonly TestDbContext _db;
    public bool SettleResult { get; set; } = true;
    public FakeInvoiceStore(TestDbContext db) => _db = db;

    public async Task<bool> SettleAsync(Guid shopId, Guid invoiceId, PaymentMethod method, string? reference,
        CancellationToken cancellationToken = default)
    {
        var shopForPrice = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        var monthlyPrice = Washtag.Domain.Plans.MonthlyPrice(shopForPrice?.Plan);

        if (!SettleResult) return false;
        var invoice = await _db.Invoices.FirstOrDefaultAsync(
            i => i.Id == invoiceId && i.ShopId == shopId &&
                 (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Overdue), cancellationToken);
        if (invoice == null) return false;

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        invoice.Method = method;
        invoice.Reference = reference;
        var remaining = await _db.Invoices.CountAsync(
            i => i.ShopId == shopId && i.Id != invoiceId &&
                 i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled, cancellationToken);
        if (remaining == 0)
        {
            var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
            // Cancelled is terminal, and a payment must never shorten a paid term.
            if (shop != null && shop.SubscriptionStatus != SubscriptionStatus.Cancelled)
            {
                var now = DateTime.UtcNow;
                shop.SubscriptionStatus = SubscriptionStatus.Active;
                if (shop.SubscriptionEndsAt <= now) shop.SubscriptionStartedAt = now;
                var end = (shop.SubscriptionEndsAt > now ? shop.SubscriptionEndsAt : now).AddMonths(1);
                shop.SubscriptionEndsAt = end;
                _db.Invoices.Add(new Invoice
                {
                    ShopId = shopId,
                    InvoiceNumber = $"INV-{now:yyyyMM}-{Guid.NewGuid().ToString("N")[..8]}",
                    PeriodFrom = now, PeriodTo = end,
                    Amount = monthlyPrice, Status = InvoiceStatus.Pending
                });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task EnsureCurrentInvoiceAsync(Guid shopId, decimal monthlyPrice, CancellationToken cancellationToken = default)
    {
        if (await _db.Invoices.AnyAsync(i => i.ShopId == shopId, cancellationToken)) return;
        var now = DateTime.UtcNow;
        var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.Invoices.Add(new Invoice
        {
            ShopId = shopId,
            InvoiceNumber = $"INV-{from:yyyyMM}-{Guid.NewGuid().ToString("N")[..8]}",
            PeriodFrom = from, PeriodTo = from.AddMonths(1),
            Amount = monthlyPrice, Status = InvoiceStatus.Pending, CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<BillingDeadlineResult> ApplyDeadlinesAsync(DateTime now, Guid? shopId = null,
        CancellationToken cancellationToken = default)
    {
        var overdue = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Pending && i.PeriodTo < now &&
                        (shopId == null || i.ShopId == shopId))
            .ToListAsync(cancellationToken);
        foreach (var invoice in overdue) invoice.Status = InvoiceStatus.Overdue;

        var candidates = overdue.Select(i => i.ShopId).ToHashSet();
        var expired = await _db.Shops
            .Where(s => s.IsActive && s.SubscriptionEndsAt <= now &&
                        (shopId == null || s.Id == shopId) &&
                        (s.SubscriptionStatus == SubscriptionStatus.Trial ||
                         s.SubscriptionStatus == SubscriptionStatus.Active))
            .Select(s => s.Id).ToListAsync(cancellationToken);
        candidates.UnionWith(expired);

        var flagged = 0;
        foreach (var candidateId in candidates)
        {
            if (await _db.Invoices.AnyAsync(
                    i => i.ShopId == candidateId && i.Status == InvoiceStatus.Paid && i.PeriodTo >= now,
                    cancellationToken)) continue;
            var shop = await _db.Shops.FirstOrDefaultAsync(
                s => s.Id == candidateId && s.IsActive && s.SubscriptionEndsAt <= now &&
                     (s.SubscriptionStatus == SubscriptionStatus.Trial ||
                      s.SubscriptionStatus == SubscriptionStatus.Active), cancellationToken);
            if (shop == null) continue;
            shop.SubscriptionStatus = SubscriptionStatus.PastDue;
            flagged++;
        }
        if (overdue.Count > 0 || flagged > 0) await _db.SaveChangesAsync(cancellationToken);
        return new BillingDeadlineResult(overdue.Count, flagged);
    }
}

public sealed class FakeAdminSettings : IAdminSettings
{
    private readonly HashSet<string> _emails;
    public FakeAdminSettings(params string[] emails) =>
        _emails = emails.Select(e => e.ToLowerInvariant()).ToHashSet();
    public bool IsAdmin(string email) => _emails.Contains(email.Trim().ToLowerInvariant());
}

public sealed class FakeAdmin : IAdminService
{
    public Task<AdminStatsDto> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AdminStatsDto { ShopCount = 1 });
    public Task<ShopDto> SetShopActiveAsync(Guid shopId, bool active, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShopDto { Id = shopId, IsActive = active });
    public Task<ShopDto> CancelShopSubscriptionAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShopDto { Id = shopId, SubscriptionStatus = SubscriptionStatus.Cancelled });
    public Task<ShopDto> RestoreSubscriptionAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ShopDto { Id = shopId, SubscriptionStatus = SubscriptionStatus.Active });
}

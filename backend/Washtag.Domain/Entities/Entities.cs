using Washtag.Domain.Enums;

namespace Washtag.Domain.Entities;

public class Shop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Plan { get; set; } = "Starter";
    public string? LogoUrl { get; set; }
    public int OrderSequence { get; set; }
    public bool IsActive { get; set; } = true;
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trial;
    public DateTime SubscriptionStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime SubscriptionEndsAt { get; set; } = DateTime.UtcNow.AddYears(1);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public bool CatalogueInitialized { get; set; }

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Staff;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Shop Shop { get; set; } = null!;
}

public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Shop Shop { get; set; } = null!;
}

public class ShopService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public decimal DefaultPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Shop Shop { get; set; } = null!;
}

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid PublicToken { get; set; } = Guid.NewGuid();
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? Notes { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Received;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Pickup;
    public string? DeliveryAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Shop Shop { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<StatusEvent> StatusHistory { get; set; } = new List<StatusEvent>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Service { get; set; } = "Wash & Fold";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public string? Notes { get; set; }

    public Order Order { get; set; } = null!;
}

public class StatusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Note { get; set; }
    public string ActorName { get; set; } = "System";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public decimal Amount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Shop Shop { get; set; } = null!;
}

public class AssistantMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShopId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "user";
    public string Text { get; set; } = string.Empty;
    public string? SourcesJson { get; set; }
    public string? Intent { get; set; }
    public bool AiPhrased { get; set; }
    public string? Provider { get; set; }
    public string? SuggestionsJson { get; set; }
    public int? Rating { get; set; }
    public string? FeedbackComment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string? SessionHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedBy { get; set; }

    public AppUser User { get; set; } = null!;
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public string RecordedBy { get; set; } = "System";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
}

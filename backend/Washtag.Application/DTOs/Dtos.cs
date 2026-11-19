using System.ComponentModel.DataAnnotations;
using Washtag.Domain.Enums;

namespace Washtag.Application.DTOs;

public class RegisterRequest
{
    [Required, MaxLength(150)] public string ShopName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string OwnerName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(150)] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [Required, MinLength(6)] public string Password { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = "Dhaka";
    public string Plan { get; set; } = "Starter";
}

public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserDto User { get; set; } = new();
    public ShopDto Shop { get; set; } = new();
}

public class UserDto
{
    public Guid Id { get; set; }
    public Guid ShopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsAdmin { get; set; }
}

public class ShopDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime SubscriptionEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateShopRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(250)] public string Address { get; set; } = string.Empty;
    [MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(30)] public string Phone { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
}

public class CreateOrderItemRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(80)] public string Service { get; set; } = "Wash & Fold";
    [Range(1, 1000)] public int Quantity { get; set; } = 1;
    [Range(0, 1000000)] public decimal UnitPrice { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public class CreateOrderRequest
{
    [Required, MaxLength(150)] public string CustomerName { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string CustomerPhone { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
    [MinLength(1)] public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class UpdateStatusRequest
{
    [Required, EnumDataType(typeof(OrderStatus))] public OrderStatus Status { get; set; }
    public string? Note { get; set; }
}

public class RecordPaymentRequest
{
    [Range(0.01, 10000000)] public decimal Amount { get; set; }
    [EnumDataType(typeof(PaymentMethod))] public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
}

public class OrderListDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PublicToken { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public int ItemCount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
}

public class StatusEventDto
{
    public Guid Id { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Note { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class OrderDetailDto : OrderListDto
{
    public string? Notes { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string TrackingUrl { get; set; } = string.Empty;
    public string QrDataUrl { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public List<StatusEventDto> StatusHistory { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public class PublicTrackDto
{
    public ShopBrandDto Shop { get; set; } = new();
    public PublicOrderDto Order { get; set; } = new();
    public List<StatusEventDto> Timeline { get; set; } = new();
}

public class ShopBrandDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
}

public class PublicOrderDto
{
    public string Token { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class DashboardDto
{
    public int TotalOrders { get; set; }
    public int ReceivedCount { get; set; }
    public int WashingCount { get; set; }
    public int IroningCount { get; set; }
    public int ReadyCount { get; set; }
    public int DeliveredCount { get; set; }
    public int CancelledCount { get; set; }
    public int ActiveCount { get; set; }
    public int UnpaidCount { get; set; }
    public decimal Collected { get; set; }
    public decimal Outstanding { get; set; }
    public int TodayOrders { get; set; }
    public int CustomerCount { get; set; }
    public List<OrderListDto> RecentOrders { get; set; } = new();
    public List<StatusCountDto> StatusBreakdown { get; set; } = new();
    public List<DayCountDto> WeeklyTrend { get; set; } = new();
}

public class StatusCountDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DayCountDto
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class CustomerDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal UnpaidAmount { get; set; }
    public DateTime LastOrderAt { get; set; }
}

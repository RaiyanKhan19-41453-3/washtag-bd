using System.ComponentModel.DataAnnotations;
using Washtag.Domain.Enums;

namespace Washtag.Application.DTOs;

public class StaffDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateStaffRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(150)] public string Email { get; set; } = string.Empty;
    [MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [Required, MinLength(6)] public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Staff;
}

public class UpdateStaffRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string Phone { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}

public class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required, MinLength(6)] public string NewPassword { get; set; } = string.Empty;
}

public class RefreshRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public decimal Amount { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class BillingOverviewDto
{
    public string Plan { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime RenewalDate { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal Outstanding { get; set; }
    public List<InvoiceDto> Invoices { get; set; } = new();
}

public class MarkInvoicePaidRequest
{
    [EnumDataType(typeof(PaymentMethod))] public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    [MaxLength(100)] public string? Reference { get; set; }
}

public class ChangePlanRequest
{
    [Required, MaxLength(50)] public string Plan { get; set; } = string.Empty;
}

public class AdminShopDto
{
    public Guid ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public SubscriptionStatus SubscriptionStatus { get; set; }
    public DateTime RenewalDate { get; set; }
    public bool IsActive { get; set; }
    public int UserCount { get; set; }
    public int OrderCount { get; set; }
    public decimal Collected { get; set; }
}

public class AdminShopActiveRequest
{
    public bool Active { get; set; }
}

public class AdminStatsDto
{
    public int ShopCount { get; set; }
    public int ActiveShopCount { get; set; }
    public int UserCount { get; set; }
    public int OrderCount { get; set; }
    public decimal Collected { get; set; }
    public List<AdminShopDto> Shops { get; set; } = new();
}

public class MaintenanceResultDto
{
    public DateTime RanAt { get; set; }
    public int OverdueInvoices { get; set; }
    public int ExpiredSubscriptions { get; set; }
    public int ShopsMarkedPastDue { get; set; }
    public int PrunedMessages { get; set; }
    public bool TokensPurged { get; set; }
    public int ReindexedShops { get; set; }
}

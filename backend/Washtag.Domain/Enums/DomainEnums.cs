namespace Washtag.Domain.Enums;

public enum OrderStatus
{
    Received,
    Washing,
    Ironing,
    ReadyForPickup,
    Delivered,
    Cancelled
}

public enum PaymentStatus
{
    Unpaid,
    Partial,
    Paid
}

public enum PaymentMethod
{
    Cash,
    Card,
    Bkash,
    Nagad,
    BankTransfer,
    Other
}

public enum UserRole
{
    Owner,
    Staff
}

public enum InvoiceStatus
{
    Pending,
    Paid,
    Overdue,
    Cancelled
}

public enum SubscriptionStatus
{
    Trial,
    Active,
    PastDue,
    Cancelled
}

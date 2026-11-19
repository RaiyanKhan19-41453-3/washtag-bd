namespace Washtag.Application;

/// <summary>Whether shop owners may settle their own invoices.</summary>
public sealed class BillingOptions
{
    public bool ManualPaymentEnabled { get; set; } = true;
}

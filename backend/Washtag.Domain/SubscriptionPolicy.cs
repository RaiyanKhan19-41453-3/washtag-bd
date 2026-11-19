using Washtag.Domain.Entities;
using Washtag.Domain.Enums;

namespace Washtag.Domain;

public static class SubscriptionPolicy
{
    public static bool IsLocked(Shop shop, DateTime? now = null)
    {
        var current = now ?? DateTime.UtcNow;
        return !shop.IsActive ||
               shop.SubscriptionStatus is SubscriptionStatus.PastDue or SubscriptionStatus.Cancelled ||
               shop.SubscriptionEndsAt <= current;
    }
}

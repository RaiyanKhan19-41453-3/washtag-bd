using Microsoft.AspNetCore.SignalR;
using Washtag.Api.Hubs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;

namespace Washtag.Api.Services;

/// <summary>Pushes order changes to the owner's shop group and the customer's token group.</summary>
public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<OrdersHub> _hub;

    public SignalRNotifier(IHubContext<OrdersHub> hub) => _hub = hub;

    public async Task OrderUpdatedAsync(Order order, object ownerPayload, object customerPayload)
    {
        await _hub.Clients.Group($"shop_{order.ShopId}").SendAsync("OrderUpdated", ownerPayload);
        await _hub.Clients.Group($"token_{order.PublicToken}").SendAsync("OrderUpdated", customerPayload);
    }
}

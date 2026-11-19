using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Hubs;

public class OrdersHub : Hub
{
    private readonly IAppDbContext _db;

    public OrdersHub(IAppDbContext db) => _db = db;

    public override async Task OnConnectedAsync()
    {
        await JoinShopGroup();
        await base.OnConnectedAsync();
    }

    public async Task JoinShopGroup()
    {
        var shopId = Context.User?.FindFirst("shopId")?.Value;
        if (Guid.TryParse(shopId, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"shop_{id}");
    }

    public async Task JoinTokenGroup(string token)
    {
        if (!Guid.TryParse(token, out var tokenId)) return;

        var rawShopId = Context.User?.FindFirst("shopId")?.Value;
        var query = _db.Orders.AsNoTracking().Where(o => o.PublicToken == tokenId);

        if (Guid.TryParse(rawShopId, out var shopId))
        {
            // Authenticated owner/staff may only follow their own shop's orders.
            if (!await query.AnyAsync(o => o.ShopId == shopId)) return;
        }
        else if (!await query.AnyAsync())
        {
            // Anonymous customer tracking: the token itself is the capability.
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"token_{tokenId}");
    }
}

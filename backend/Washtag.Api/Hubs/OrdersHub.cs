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
        if (await _db.Orders.AnyAsync(o => o.PublicToken == tokenId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"token_{tokenId}");
    }
}

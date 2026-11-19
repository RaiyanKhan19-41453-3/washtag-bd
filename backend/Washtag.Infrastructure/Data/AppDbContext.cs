using Microsoft.EntityFrameworkCore;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;

namespace Washtag.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
        mb.Entity<Shop>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => new { x.SubscriptionStatus, x.SubscriptionEndsAt });
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(150);
        });

        mb.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.ShopId);
            e.HasOne(x => x.Shop).WithMany(x => x.Users).HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Customer>(e =>
        {
            e.HasIndex(x => new { x.ShopId, x.Phone }).IsUnique();
            e.HasOne(x => x.Shop).WithMany(x => x.Customers).HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Order>(e =>
        {
            e.HasIndex(x => x.PublicToken).IsUnique();
            e.HasIndex(x => new { x.ShopId, x.OrderNumber }).IsUnique();
            e.HasIndex(x => new { x.ShopId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.ShopId, x.CreatedAt });
            e.HasIndex(x => new { x.ShopId, x.Status });
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Shop).WithMany(x => x.Orders).HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<OrderItem>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<StatusEvent>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.HasOne(x => x.Order).WithMany(x => x.StatusHistory).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Invoice>(e =>
        {
            e.HasIndex(x => x.InvoiceNumber).IsUnique();
            e.HasIndex(x => x.ShopId);
            e.HasIndex(x => new { x.ShopId, x.Status });
            e.HasIndex(x => new { x.Status, x.PeriodTo });
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Shop).WithMany(x => x.Invoices).HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<AssistantMessage>(e =>
        {
            e.HasIndex(x => new { x.ShopId, x.UserId, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
            e.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;

namespace Washtag.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Shops.AnyAsync()) return;

        var now = DateTime.UtcNow;
        var shop = new Shop
        {
            Name = "Shine & Fold Laundry",
            OwnerName = "Rahim Ahmed",
            Email = "demo@washtagbd.com",
            Phone = "+880 1712-345678",
            Address = "12/A, Road 7, Dhanmondi",
            City = "Dhaka",
            Plan = "Growth",
            IsActive = true,
            SubscriptionEndsAt = now.AddMonths(11),
            CreatedAt = now.AddMonths(2)
        };

        var owner = new AppUser
        {
            Shop = shop,
            Name = "Rahim Ahmed",
            Email = "demo@washtagbd.com",
            Phone = "+880 1712-345678",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo1234"),
            Role = UserRole.Owner,
            IsActive = true,
            CreatedAt = now.AddMonths(2)
        };

        var staff = new AppUser
        {
            Shop = shop,
            Name = "Sumaiya Akter",
            Email = "staff@washtagbd.com",
            Phone = "+880 1819-887766",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo1234"),
            Role = UserRole.Staff,
            IsActive = true,
            CreatedAt = now.AddMonths(1)
        };

        db.Users.AddRange(owner, staff);

        var people = new[]
        {
            ("Tasnim Rahman", "+880 1813-220011"),
            ("Imran Hossain", "+880 1915-334455"),
            ("Nadia Islam", "+880 1755-908070"),
            ("Fahad Karim", "+880 1620-112233"),
            ("Sadia Jahan", "+880 1822-445566"),
            ("Rakibul Hasan", "+880 1933-778899"),
            ("Mita Biswas", "+880 1799-001122"),
            ("Arif Chowdhury", "+880 1611-556677"),
            ("Jubair Alam", "+880 1888-990011"),
            ("Lamya Nusrat", "+880 1777-334455")
        };

        var customers = people.Select(p => new Customer
        {
            Shop = shop,
            Name = p.Item1,
            Phone = p.Item2,
            CreatedAt = now.AddDays(-40)
        }).ToList();
        db.Customers.AddRange(customers);

        var serviceNames = new[] { "Wash & Fold", "Dry Clean", "Iron Only", "Wash & Iron", "Tailoring" };
        var clothNames = new[] { "Cotton Shirt", "Formal Trouser", "Winter Coat", "Saree", "T-Shirt", "Jeans", "Kurta", "Blazer", "Dupatta", "Panjabi" };
        var statuses = new[]
        {
            OrderStatus.Received, OrderStatus.Washing, OrderStatus.Ironing, OrderStatus.ReadyForPickup,
            OrderStatus.Delivered, OrderStatus.Delivered, OrderStatus.ReadyForPickup, OrderStatus.Washing,
            OrderStatus.Received, OrderStatus.Delivered, OrderStatus.Ironing, OrderStatus.Received,
            OrderStatus.ReadyForPickup, OrderStatus.Delivered
        };

        var random = new Random(42);
        for (var i = 0; i < statuses.Length; i++)
        {
            var status = statuses[i];
            var createdAt = now.AddDays(-i).AddHours(-random.Next(1, 9));
            var person = people[i % people.Length];
            shop.OrderSequence += 1;

            var order = new Order
            {
                Shop = shop,
                OrderNumber = $"WT-{shop.OrderSequence:D5}",
                PublicToken = Guid.NewGuid(),
                CustomerName = person.Item1,
                CustomerPhone = person.Item2,
                Notes = i % 4 == 0 ? "Please be careful with the collar." : null,
                Status = status,
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddHours(random.Next(2, 30)),
                DueDate = createdAt.AddDays(3),
                DeliveredAt = status == OrderStatus.Delivered ? createdAt.AddDays(2).AddHours(4) : null
            };

            var itemCount = 1 + random.Next(0, 3);
            decimal total = 0;
            for (var j = 0; j < itemCount; j++)
            {
                var qty = 1 + random.Next(0, 4);
                var price = 60 + random.Next(0, 12) * 20;
                var item = new OrderItem
                {
                    Order = order,
                    Name = clothNames[(i + j) % clothNames.Length],
                    Service = serviceNames[(i + j) % serviceNames.Length],
                    Quantity = qty,
                    UnitPrice = price
                };
                total += qty * price;
                order.Items.Add(item);
            }
            order.TotalAmount = total;

            var maxIndex = (int)status;
            if (status == OrderStatus.Delivered) maxIndex = (int)OrderStatus.Delivered;
            for (var s = 0; s <= maxIndex; s++)
            {
                order.StatusHistory.Add(new StatusEvent
                {
                    Order = order,
                    FromStatus = s == 0 ? null : (OrderStatus)(s - 1),
                    ToStatus = (OrderStatus)s,
                    Note = s == 0 ? "Order received from customer" : null,
                    ActorName = s % 2 == 0 ? "Rahim Ahmed" : "Sumaiya Akter",
                    CreatedAt = createdAt.AddHours(s * 5)
                });
            }

            decimal paid = 0;
            if (status == OrderStatus.Delivered) paid = i == 4 ? total * 0.5m : total;
            else if (status == OrderStatus.ReadyForPickup && i % 2 == 1) paid = total * 0.5m;
            else if (status == OrderStatus.Ironing && i % 3 == 0) paid = total * 0.4m;

            order.PaidAmount = paid;
            order.PaymentStatus = paid <= 0 ? PaymentStatus.Unpaid
                : paid >= total ? PaymentStatus.Paid : PaymentStatus.Partial;

            if (paid > 0)
            {
                order.Payments.Add(new Payment
                {
                    Order = order,
                    Amount = paid,
                    Method = i % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.Bkash,
                    Reference = i % 2 == 0 ? null : $"BK-{1000 + i}",
                    RecordedBy = "Rahim Ahmed",
                    CreatedAt = createdAt.AddHours(10)
                });
            }

            db.Orders.Add(order);
        }

        await db.SaveChangesAsync();
    }
}

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool seedDemo)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        try
        {
            db.Database.EnsureCreated();
            logger.LogInformation("Database ready.");
            if (seedDemo)
            {
                await DbSeeder.SeedAsync(services);
                logger.LogInformation("Demo data check complete.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database initialization failed; the API will start but data operations may fail.");
        }
    }
}

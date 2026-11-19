using Microsoft.Extensions.DependencyInjection;
using Washtag.Application.Interfaces;
using Washtag.Application.Services;

namespace Washtag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAssistantService, AssistantService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        return services;
    }
}

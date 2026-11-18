using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Washtag.Api.Hubs;
using Washtag.Api.Middleware;
using Washtag.Api.Services;
using Washtag.Application;
using Washtag.Application.Interfaces;
using Washtag.Infrastructure;
using Washtag.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITrackingUrlProvider, TrackingUrlProvider>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<IAdminSettings, AdminSettings>();

// Never run Production on the source-checked development secret: mint an
// ephemeral one instead so tokens cannot be forged with a publicly known key.
const string defaultSecret = "washtagbd-development-secret-change-this-in-production-2026";
var configuredSecret = builder.Configuration["JWT_SECRET"];
var usingDefaultSecret = string.IsNullOrWhiteSpace(configuredSecret) || configuredSecret == defaultSecret;
if (usingDefaultSecret && builder.Environment.IsProduction())
{
    configuredSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    builder.Configuration["JWT_SECRET"] = configuredSecret;
}
var jwtSecret = configuredSecret ?? defaultSecret;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JWT_ISSUER"] ?? "WashtagBD",
        ValidAudience = builder.Configuration["JWT_AUDIENCE"] ?? "WashtagBDClient",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                context.Token = token;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "WashTag BD API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter: Bearer {your JWT token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddSignalR();
var allowedOrigins = (builder.Configuration["ALLOWED_ORIGINS"] ?? "*")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("App", policy =>
{
    if (allowedOrigins.Contains("*"))
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("ai", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many attempts. Please try again in a minute." }, token);
    };
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionMiddleware>();
// Swagger is opt-in in production; the demo compose file enables it by default.
if (builder.Configuration.GetValue("ENABLE_SWAGGER", builder.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WashTag BD API v1");
        c.RoutePrefix = "swagger";
    });
}
app.UseCors("App");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<OrdersHub>("/hubs/orders");
app.MapGet("/health", async (IServiceProvider services) =>
{
    if (!DbInitializer.InitializationSucceeded)
        return Results.Json(new { status = "unhealthy", reason = "database initialization failed" }, statusCode: 503);
    try
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Shops.AnyAsync();
        return Results.Json(new { status = "healthy", checkedAt = DateTime.UtcNow });
    }
    catch
    {
        return Results.Json(new { status = "unhealthy" }, statusCode: 503);
    }
}).AllowAnonymous();

var seedFlag = Environment.GetEnvironmentVariable("SEED_DEMO_DATA")
    ?? builder.Configuration["SEED_DEMO_DATA"] ?? "true";
if (usingDefaultSecret && app.Environment.IsProduction())
    app.Logger.LogWarning("JWT_SECRET is unset or the development default; using an ephemeral secret. Tokens will reset on restart.");

var seedDemo = seedFlag.Equals("true", StringComparison.OrdinalIgnoreCase);
await DbInitializer.InitializeAsync(app.Services, seedDemo, app.Environment.IsProduction());
await AssistantInitializer.InitializeAsync(app.Services);

app.Run();

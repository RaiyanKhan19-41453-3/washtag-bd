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
using Washtag.Api.Workers;
using Washtag.Application;
using Washtag.Application.Interfaces;
using Washtag.Infrastructure;
using Washtag.Infrastructure.Data;
using Washtag.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITrackingUrlProvider, TrackingUrlProvider>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<IAdminSettings, AdminSettings>();

var maintenance = new MaintenanceOptions
{
    Enabled = builder.Configuration.GetValue("MAINTENANCE_ENABLED", true),
    IntervalMinutes = builder.Configuration.GetValue("MAINTENANCE_INTERVAL_MINUTES", 60),
    // The assistant initializer already reindexes every shop at startup; avoid a
    // second full sweep 20 seconds later.
    RunOnStartup = builder.Configuration.GetValue("MAINTENANCE_RUN_ON_STARTUP", false),
    // Zero or negative disables chat retention rather than silently becoming "1 day".
    ChatRetentionDays = builder.Configuration.GetValue("MAINTENANCE_CHAT_RETENTION_DAYS", 90)
};
builder.Services.AddSingleton(maintenance);
builder.Services.AddSingleton(new BillingOptions
{
    ManualPaymentEnabled = builder.Configuration.GetValue("MANUAL_PAYMENT_ENABLED", false)
});
if (maintenance.Enabled) builder.Services.AddHostedService<MaintenanceWorker>();

// Never run Production on a weak or source-checked secret: mint an ephemeral one
// instead so tokens cannot be forged. Note that this logs everyone out on restart,
// so a real deployment must set a strong shared JWT_SECRET.
const string defaultSecret = "washtagbd-development-secret-change-this-in-production-2026";
var configuredSecret = builder.Configuration["JWT_SECRET"]?.Trim();
var usingWeakSecret = string.IsNullOrWhiteSpace(configuredSecret) ||
    configuredSecret.Equals(defaultSecret, StringComparison.Ordinal) ||
    (builder.Environment.IsProduction() && configuredSecret.Length < 32);
if (usingWeakSecret && builder.Environment.IsProduction())
{
    configuredSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    builder.Configuration["JWT_SECRET"] = configuredSecret;
}
var jwtSecret = configuredSecret ?? defaultSecret;
// The token generator reads configuration directly - keep it in sync with the
// value the validation key actually uses (including trimming).
builder.Configuration["JWT_SECRET"] = jwtSecret;

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
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                context.Token = token;
            return Task.CompletedTask;
        },
        // A deactivated account must not keep using an already-issued access token.
        OnTokenValidated = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<IAppDbContext>();
            var raw = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.Principal?.FindFirstValue("sub");
            if (!Guid.TryParse(raw, out var userId))
            {
                context.Fail("Invalid token.");
                return;
            }
            var user = await db.Users.AsNoTracking().Include(u => u.Shop)
                .FirstOrDefaultAsync(u => u.Id == userId, context.HttpContext.RequestAborted);
            if (user == null || !user.IsActive)
                context.Fail("This account is inactive.");
            else if (context.Principal?.FindFirstValue("ver") != TokenVersion.From(user.PasswordHash))
                context.Fail("Password has changed.");
            else
            {
                context.HttpContext.Items["SubscriptionLocked"] = SubscriptionPolicy.IsLocked(user.Shop);
                if (!user.Shop.IsActive)
                    context.HttpContext.Items["SubscriptionLocked"] = true;
            }
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
var allowedOrigins = (builder.Configuration["ALLOWED_ORIGINS"] ?? "http://localhost:8081")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var emptyAllowedOrigins = allowedOrigins.Length == 0;
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
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("reindex", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("reports", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
    options.AddPolicy("webhook", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

    options.AddPolicy("maintenance", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    options.AddPolicy("public", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many attempts. Please try again in a minute." }, token);
    };
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Only trust X-Forwarded-For from private proxy networks; a client cannot
    // mint a fresh rate-limit bucket by spoofing the header from the internet.
    options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
    options.KnownProxies.Clear();
});

var app = builder.Build();

var seedFlagRaw = Environment.GetEnvironmentVariable("SEED_DEMO_DATA")
    ?? app.Configuration["SEED_DEMO_DATA"] ?? "true";
var normalizedSeed = seedFlagRaw.Trim();
var seedDemo = normalizedSeed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalizedSeed is "1" or "yes" or "on";

if (app.Environment.IsProduction() &&
    app.Configuration.GetValue("ADMIN_EMAILS", string.Empty).Contains("demo@washtagbd.com", StringComparison.OrdinalIgnoreCase) &&
    !app.Configuration.GetValue("ALLOW_DEMO_IN_PRODUCTION", false))
{
    throw new InvalidOperationException(
        "Refusing to start: Production grants platform admin to the public demo address (seeding flag: " +
        seedDemo + "). Use docker-compose.prod.yml with a fresh pgdata volume, an empty ADMIN_EMAILS for " +
        "bootstrap, and your own owner account. Set ALLOW_DEMO_IN_PRODUCTION=true only for an intentionally public demo.");
}
var configuredAdmins = (app.Configuration["ADMIN_EMAILS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
app.Logger.LogInformation("Platform admin accounts configured: {Count}", configuredAdmins);
if (configuredAdmins == 0 && app.Environment.IsProduction())
    app.Logger.LogWarning("ADMIN_EMAILS is empty; register an owner first, then set it and restart.");

if (emptyAllowedOrigins && app.Environment.IsProduction())
    app.Logger.LogWarning("ALLOWED_ORIGINS is empty; only same-origin requests through the web proxy will work.");
if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(app.Configuration["PUBLIC_BASE_URL"]))
    app.Logger.LogWarning("PUBLIC_BASE_URL is unset; QR codes will use the request host. Set it to https://your.domain for stable, trustworthy links.");

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionMiddleware>();
// Swagger is opt-in; set ENABLE_SWAGGER=true to expose interactive API docs.
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
app.UseAuthentication();
app.UseAuthorization();

// Locked shops keep access to auth, billing and shop endpoints so the owner can
// renew; everything else is blocked with a clear message.
string[] renewalPaths = { "/api/auth", "/api/billing", "/api/shop", "/api/public", "/api/admin", "/hubs", "/swagger", "/health" };
app.Use(async (context, next) =>
{
    if (context.User?.Identity?.IsAuthenticated == true &&
        context.Items.TryGetValue("SubscriptionLocked", out var locked) && locked is true &&
        !renewalPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Your subscription is inactive. Renew from the Billing page to continue."
        });
        return;
    }
    await next();
});

// Rate-limit after authentication so per-user partition keys are available.
app.UseRateLimiter();
app.MapControllers();
app.MapHub<OrdersHub>("/hubs/orders");
app.MapGet("/health", async (IServiceProvider services) =>
{
    // Probe the live database rather than a one-shot startup flag: a transient
    // boot failure should not mark a healthy deployment as permanently down.
    try
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Shops.AnyAsync();
        if (!DbInitializer.InitializationSucceeded)
            return Results.Json(new { status = "unhealthy", reason = "database initialization incomplete" }, statusCode: 503);
        return Results.Json(new { status = "healthy", initialized = true, checkedAt = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Health probe failed: database is unreachable.");
        return Results.Json(new { status = "unhealthy", reason = "database unreachable" }, statusCode: 503);
    }
}).AllowAnonymous();

if (usingWeakSecret && app.Environment.IsProduction())
    app.Logger.LogWarning("JWT_SECRET is unset, weak, or the development default; using an ephemeral secret. Tokens will reset on restart.");
foreach (var delay in new[] { 0, 2, 5, 10, 20 })
{
    if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay));
    try
    {
        await DbInitializer.InitializeAsync(app.Services, seedDemo, app.Environment.IsProduction());
        break;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database initialization attempt failed (next retry in {Delay}s).", delay);
    }
}

// A long startup outage must not leave /health permanently 503: keep retrying
// in the background until initialization succeeds or the app shuts down.
if (!DbInitializer.InitializationSucceeded)
{
    _ = Task.Run(async () =>
    {
        while (!DbInitializer.InitializationSucceeded &&
               !app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), app.Lifetime.ApplicationStopping);
            try { await DbInitializer.InitializeAsync(app.Services, seedDemo, app.Environment.IsProduction()); }
            catch (Exception ex) { app.Logger.LogError(ex, "Deferred database initialization failed; retrying in 30s."); }
        }
    });
}

// Index in the background so AI/gateway latency never blocks readiness.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        foreach (var delay in new[] { 0, 5, 15 })
        {
            if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay));
            try
            {
                await AssistantInitializer.InitializeAsync(app.Services);
                return;
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Background assistant index attempt failed (next retry in {Delay}s).", delay);
            }
        }
    });
});

app.Run();

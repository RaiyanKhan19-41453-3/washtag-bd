using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Washtag.Api.Middleware;
using Washtag.Domain.Exceptions;
using Xunit;

namespace Washtag.Api.Tests;

public class ExceptionMiddlewareTests
{
    private static async Task<(int Status, string Body)> RunAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        var middleware = new ExceptionMiddleware(
            _ => throw exception,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        body.Position = 0;
        return (context.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task DomainException_Returns400WithMessage()
    {
        var (status, body) = await RunAsync(new AppException("Invalid email or password."));
        Assert.Equal(400, status);
        Assert.Contains("Invalid email or password.", body);
    }

    [Fact]
    public async Task ConcurrencyException_Returns409()
    {
        var (status, body) = await RunAsync(new DbUpdateConcurrencyException());
        Assert.Equal(409, status);
        Assert.Contains("record changed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateException_Returns409()
    {
        var (status, body) = await RunAsync(new DbUpdateException("duplicate"));
        Assert.Equal(409, status);
        Assert.Contains("already exists", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownException_Returns500WithoutDetails()
    {
        var (status, body) = await RunAsync(new Exception("connection string secret"));
        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body);
        Assert.Contains("Something went wrong", body);
    }
}

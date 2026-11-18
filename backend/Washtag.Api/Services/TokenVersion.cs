using System.Security.Cryptography;
using System.Text;

namespace Washtag.Api.Services;

/// <summary>Derives a short version from the password hash so changing a password
/// invalidates all previously issued access tokens.</summary>
public static class TokenVersion
{
    public static string From(string passwordHash) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];
}

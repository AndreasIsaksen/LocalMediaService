using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using LocalMediaService.Web.Options;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Web.Services;

public sealed class AdminCredentialVerifier(IOptions<PortalSecurityOptions> options)
{
    public const string SessionStampClaim = "lms:session-stamp";

    private readonly byte[] _expectedUsername = Hash(options.Value.AdminUsername);
    private readonly byte[] _expectedPassword = Hash(options.Value.AdminPassword);

    public string SessionStamp { get; } = Convert.ToHexString(Hash(options.Value.AdminPassword));

    public bool Verify(string? username, string? password)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_expectedUsername, Hash(username)) &
               CryptographicOperations.FixedTimeEquals(_expectedPassword, Hash(password));
    }

    public bool VerifyPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_expectedPassword, Hash(password));
    }

    public bool IsCurrentSession(ClaimsPrincipal? principal)
    {
        var stamp = principal?.FindFirstValue(SessionStampClaim);
        if (string.IsNullOrEmpty(stamp))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(SessionStamp),
            Encoding.UTF8.GetBytes(stamp));
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

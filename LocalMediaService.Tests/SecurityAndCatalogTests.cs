using LocalMediaService.Web.Options;
using LocalMediaService.Web.Services;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace LocalMediaService.Tests;

public sealed class SecurityAndCatalogTests
{
    [Fact]
    public void AdminVerifier_RequiresExactUsernameAndPassword()
    {
        var verifier = new AdminCredentialVerifier(Options.Create(new PortalSecurityOptions
        {
            AdminUsername = "admin",
            AdminPassword = "a-long-test-password"
        }));

        Assert.True(verifier.Verify("admin", "a-long-test-password"));
        Assert.False(verifier.Verify("Admin", "a-long-test-password"));
        Assert.False(verifier.Verify("admin", "wrong-password"));
        Assert.True(verifier.VerifyPassword("a-long-test-password"));

        var currentPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AdminCredentialVerifier.SessionStampClaim, verifier.SessionStamp)
        ]));
        var stalePrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AdminCredentialVerifier.SessionStampClaim, "old-password-stamp")
        ]));
        Assert.True(verifier.IsCurrentSession(currentPrincipal));
        Assert.False(verifier.IsCurrentSession(stalePrincipal));
    }

    [Fact]
    public void CatalogValidation_RejectsHttpUrlsAndDuplicateIds()
    {
        var valid = new StreamingServiceDefinition
        {
            Id = "service-one",
            Name = "Service One",
            HomeUrl = "https://example.com/",
            LoginUrl = "https://example.com/login"
        };

        Assert.True(StreamingServiceCatalog.IsValid([valid]));
        Assert.False(StreamingServiceCatalog.IsValid([valid, valid]));
        Assert.False(StreamingServiceCatalog.IsValid([
            new StreamingServiceDefinition
            {
                Id = "unsafe",
                Name = "Unsafe",
                HomeUrl = "http://example.com/",
                LoginUrl = "https://example.com/"
            }
        ]));
    }
}

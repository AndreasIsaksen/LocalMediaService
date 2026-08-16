using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using LocalMediaService.Web.Endpoints;
using LocalMediaService.Web.Options;
using LocalMediaService.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

if (args is ["--healthcheck", var healthUrl])
{
    return await RunHealthCheckAsync(healthUrl);
}

var builder = WebApplication.CreateBuilder(args);
var requireHttps = builder.Configuration.GetValue($"{PortalSecurityOptions.SectionName}:RequireHttps", true);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddOptions<MediaLibraryOptions>()
    .Bind(builder.Configuration.GetSection(MediaLibraryOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "MediaLibrary:RootPath is required.")
    .Validate(options => string.IsNullOrEmpty(options.MountSentinelFile) ||
                         (!Path.IsPathRooted(options.MountSentinelFile) &&
                          options.MountSentinelFile.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0),
        "MediaLibrary:MountSentinelFile must be empty or a single filename.")
    .Validate(options => options.ScanIntervalSeconds is >= 5 and <= 3600,
        "MediaLibrary:ScanIntervalSeconds must be between 5 and 3600.")
    .ValidateOnStart();

builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.DataPath), "Storage:DataPath is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<PortalSecurityOptions>()
    .Bind(builder.Configuration.GetSection(PortalSecurityOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.AdminUsername),
        "PortalSecurity:AdminUsername is required.")
    .Validate(options => options.AdminPassword?.Length >= 12,
        "PortalSecurity:AdminPassword must contain at least 12 characters.")
    .ValidateOnStart();

builder.Services
    .AddOptions<CredentialStoreOptions>()
    .Bind(builder.Configuration.GetSection(CredentialStoreOptions.SectionName))
    .Validate(options => CredentialEncryption.IsValidKey(options.EncryptionKey),
        "CredentialStore:EncryptionKey must be a Base64-encoded 32-byte key.")
    .ValidateOnStart();

builder.Services
    .AddOptions<StreamingServicesOptions>()
    .Bind(builder.Configuration.GetSection(StreamingServicesOptions.SectionName))
    .Validate(options => StreamingServiceCatalog.IsValid(options.Services),
        "Every streaming service needs a unique safe id, a name, and HTTPS home/login URLs.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "LocalMediaService.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = requireHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var verifier = context.HttpContext.RequestServices.GetRequiredService<AdminCredentialVerifier>();
            if (verifier.IsCurrentSession(context.Principal))
            {
                return;
            }

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        };
    });
builder.Services.AddAuthorization();

var configuredDataPath = Path.GetFullPath(
    builder.Configuration[$"{StorageOptions.SectionName}:DataPath"] ?? "/data");
builder.Services
    .AddDataProtection()
    .SetApplicationName("LocalMediaService")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(configuredDataPath, "keys")));

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "LocalMediaService.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = requireHttps
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
            AutoReplenishment = true
        }));
    options.AddPolicy("reveal", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.Identity?.Name ?? "anonymous"}:{context.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(5),
            AutoReplenishment = true
        }));
});

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<AdminCredentialVerifier>();
builder.Services.AddSingleton<CredentialEncryption>();
builder.Services.AddSingleton<EncryptedCredentialStore>();
builder.Services.AddSingleton<MediaLibrary>();
builder.Services.AddSingleton<StreamingServiceCatalog>();

var app = builder.Build();

var storageOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
var storagePath = Path.GetFullPath(storageOptions.DataPath);
Directory.CreateDirectory(storagePath);
Directory.CreateDirectory(Path.Combine(storagePath, "keys"));

app.UseExceptionHandler();
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
{
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
}
app.UseForwardedHeaders(forwardedHeadersOptions);

app.Use(async (context, next) =>
{
    if (requireHttps &&
        !context.Request.IsHttps &&
        !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await context.Response.WriteAsJsonAsync(new
        {
            title = "HTTPS is required.",
            status = StatusCodes.Status426UpgradeRequired
        });
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.TryAdd("Content-Security-Policy",
            "default-src 'self'; base-uri 'none'; connect-src 'self'; font-src 'self'; form-action 'self'; frame-ancestors 'none'; img-src 'self' data:; media-src 'self'; object-src 'none'; script-src 'self'; style-src 'self'");
        headers.TryAdd("Referrer-Policy", "no-referrer");
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
        return Task.CompletedTask;
    });

    await next();
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPortalEndpoints();

await app.RunAsync();
return 0;

static async Task<int> RunHealthCheckAsync(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var healthUri) ||
        healthUri.Scheme is not ("http" or "https"))
    {
        return 2;
    }

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync(healthUri);
        return response.StatusCode == HttpStatusCode.OK ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

public partial class Program;

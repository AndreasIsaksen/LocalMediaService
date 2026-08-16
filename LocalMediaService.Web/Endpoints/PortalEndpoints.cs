using System.Security.Claims;
using System.Text;
using LocalMediaService.Web.Models;
using LocalMediaService.Web.Options;
using LocalMediaService.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Web.Endpoints;

public static class PortalEndpoints
{
    public static void MapPortalEndpoints(this WebApplication app)
    {
        var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

        app.MapGet("/login", () => Results.File(Path.Combine(webRoot, "login.html"), "text/html"))
            .AllowAnonymous();
        app.MapGet("/", () => Results.File(Path.Combine(webRoot, "index.html"), "text/html"))
            .RequireAuthorization();

        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
            .AllowAnonymous();
        app.MapGet("/health/ready", ReadyAsync)
            .AllowAnonymous();

        app.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", LogoutAsync)
            .RequireAuthorization();
        app.MapGet("/api/auth/session", GetSession)
            .AllowAnonymous();

        app.MapGet("/api/services", GetServicesAsync)
            .RequireAuthorization();
        app.MapPost("/api/accounts", CreateAccountAsync)
            .RequireAuthorization();
        app.MapPut("/api/accounts/{id:guid}", UpdateAccountAsync)
            .RequireAuthorization();
        app.MapDelete("/api/accounts/{id:guid}", DeleteAccountAsync)
            .RequireAuthorization();
        app.MapPost("/api/accounts/{id:guid}/reveal", RevealAccountAsync)
            .RequireAuthorization()
            .RequireRateLimiting("reveal");

        app.MapGet("/api/media", GetMediaAsync)
            .RequireAuthorization();
        app.MapPost("/api/media/rescan", RescanMediaAsync)
            .RequireAuthorization();
        app.MapMethods("/api/media/{id}/content", ["GET", "HEAD"], StreamMediaAsync)
            .RequireAuthorization();
        app.MapGet("/api/media/{mediaId}/subtitles/{subtitleId}", GetSubtitleAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> ReadyAsync(
        MediaLibrary mediaLibrary,
        EncryptedCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var mediaReady = mediaLibrary.IsRootAvailable();
        var credentialStoreReady = await credentialStore.IsReadableAsync(cancellationToken);
        return mediaReady && credentialStoreReady
            ? Results.Ok(new { status = "ready" })
            : Results.Json(
                new { status = "not-ready", mediaMounted = mediaReady, credentialStoreReadable = credentialStoreReady },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        AdminCredentialVerifier verifier,
        IOptions<PortalSecurityOptions> options)
    {
        if (!verifier.Verify(request.Username, request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid username or password.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, options.Value.AdminUsername),
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim(AdminCredentialVerifier.SessionStampClaim, verifier.SessionStamp)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            AllowRefresh = true
        };
        if (request.RememberMe)
        {
            properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
        }

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { authenticated = true, username = options.Value.AdminUsername });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var csrfError = await ValidateAntiforgeryAsync(context, antiforgery);
        if (csrfError is not null)
        {
            return csrfError;
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static IResult GetSession(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(new { authenticated = false });
        }

        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new
        {
            authenticated = true,
            username = context.User.Identity.Name,
            csrfToken = tokens.RequestToken
        });
    }

    private static async Task<IResult> GetServicesAsync(
        StreamingServiceCatalog catalog,
        EncryptedCredentialStore credentialStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var accounts = await credentialStore.ListAsync(cancellationToken);
        var response = catalog.Services.Select(service => new StreamingServiceResponse(
            service.Id,
            service.Name,
            service.HomeUrl,
            service.LoginUrl,
            service.Description,
            service.Accent,
            accounts.Where(account => account.ServiceId == service.Id).ToArray()));
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateAccountAsync(
        CredentialInput input,
        HttpContext context,
        IAntiforgery antiforgery,
        StreamingServiceCatalog catalog,
        EncryptedCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var csrfError = await ValidateAntiforgeryAsync(context, antiforgery);
        if (csrfError is not null)
        {
            return csrfError;
        }

        var validationError = ValidateCredential(input, catalog);
        if (validationError is not null)
        {
            return validationError;
        }

        var saved = await credentialStore.SaveAsync(null, input, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Created($"/api/accounts/{saved.Id:D}", saved);
    }

    private static async Task<IResult> UpdateAccountAsync(
        Guid id,
        CredentialInput input,
        HttpContext context,
        IAntiforgery antiforgery,
        StreamingServiceCatalog catalog,
        EncryptedCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var csrfError = await ValidateAntiforgeryAsync(context, antiforgery);
        if (csrfError is not null)
        {
            return csrfError;
        }

        var validationError = ValidateCredential(input, catalog);
        if (validationError is not null)
        {
            return validationError;
        }

        try
        {
            var saved = await credentialStore.SaveAsync(id, input, cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(saved);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> DeleteAccountAsync(
        Guid id,
        HttpContext context,
        IAntiforgery antiforgery,
        EncryptedCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var csrfError = await ValidateAntiforgeryAsync(context, antiforgery);
        if (csrfError is not null)
        {
            return csrfError;
        }

        return await credentialStore.DeleteAsync(id, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> RevealAccountAsync(
        Guid id,
        RevealCredentialRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        AdminCredentialVerifier verifier,
        EncryptedCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var csrfError = await ValidateAntiforgeryAsync(context, antiforgery);
        if (csrfError is not null)
        {
            return csrfError;
        }

        if (!verifier.VerifyPassword(request.AdminPassword))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Administrator password confirmation failed.");
        }

        var credential = await credentialStore.RevealAsync(id, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return credential is null ? Results.NotFound() : Results.Ok(credential);
    }

    private static async Task<IResult> GetMediaAsync(
        MediaLibrary mediaLibrary,
        CancellationToken cancellationToken)
    {
        var snapshot = await mediaLibrary.GetSnapshotAsync(false, cancellationToken);
        return Results.Ok(new
        {
            rootAvailable = snapshot.RootAvailable,
            scannedAt = snapshot.ScannedAt,
            warnings = snapshot.Warnings,
            items = snapshot.Items.Select(ToResponse)
        });
    }

    private static async Task<IResult> RescanMediaAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        MediaLibrary mediaLibrary,
        CancellationToken cancellationToken)
    {
        var csrfError = await ValidateAntiforgeryAsync(context, antiforgery);
        if (csrfError is not null)
        {
            return csrfError;
        }

        var snapshot = await mediaLibrary.GetSnapshotAsync(true, cancellationToken);
        return Results.Ok(new
        {
            rootAvailable = snapshot.RootAvailable,
            scannedAt = snapshot.ScannedAt,
            warnings = snapshot.Warnings,
            items = snapshot.Items.Select(ToResponse)
        });
    }

    private static async Task<IResult> StreamMediaAsync(
        string id,
        HttpContext context,
        MediaLibrary mediaLibrary,
        CancellationToken cancellationToken)
    {
        var opened = await mediaLibrary.OpenMediaAsync(id, cancellationToken);
        if (opened is null)
        {
            return Results.NotFound();
        }

        context.Response.RegisterForDispose(opened.Stream);
        return Results.Stream(
            opened.Stream,
            opened.Media.ContentType,
            enableRangeProcessing: true,
            lastModified: opened.Media.LastModified);
    }

    private static async Task<IResult> GetSubtitleAsync(
        string mediaId,
        string subtitleId,
        HttpContext context,
        MediaLibrary mediaLibrary,
        CancellationToken cancellationToken)
    {
        var opened = await mediaLibrary.OpenSubtitleAsync(mediaId, subtitleId, cancellationToken);
        if (opened is null)
        {
            return Results.NotFound();
        }

        var webVtt = await SrtToVttConverter.ConvertStreamAsync(opened.Stream, cancellationToken);
        context.Response.Headers.CacheControl = "private, max-age=300";
        return Results.Text(webVtt, "text/vtt", Encoding.UTF8);
    }

    private static MediaItemResponse ToResponse(MediaFile media) => new(
        media.Id,
        media.Title,
        media.RelativePath,
        media.Category,
        media.SizeBytes,
        media.LastModified,
        media.ContentType,
        $"/api/media/{media.Id}/content",
        media.DirectPlayLikely,
        media.Subtitles.Select(subtitle => new SubtitleTrackResponse(
            subtitle.Id,
            subtitle.Label,
            subtitle.Language,
            $"/api/media/{media.Id}/subtitles/{subtitle.Id}")).ToArray());

    private static IResult? ValidateCredential(CredentialInput input, StreamingServiceCatalog catalog)
    {
        if (!catalog.Contains(input.ServiceId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["serviceId"] = ["Select a configured streaming service."]
            });
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Label) || input.Label.Length > 80)
        {
            errors["label"] = ["Account label is required and must be at most 80 characters."];
        }

        if (string.IsNullOrWhiteSpace(input.Username) || input.Username.Length > 320)
        {
            errors["username"] = ["Username is required and must be at most 320 characters."];
        }

        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length > 1024)
        {
            errors["password"] = ["Password is required and must be at most 1024 characters."];
        }

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static async Task<IResult?> ValidateAntiforgeryAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The request security token is missing or invalid.");
        }
    }
}

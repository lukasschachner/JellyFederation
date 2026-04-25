using System.Text.Json;
using JellyFederation.Data;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Services;

public sealed class WebSessionService
{
    public const string CookieName = "jf_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private readonly FederationDbContext _db;
    private readonly IDataProtector _protector;

    public WebSessionService(FederationDbContext db, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("JellyFederation.WebSession.v1");
    }

    public CookieOptions CreateCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = SessionLifetime,
        Path = "/"
    };

    public string CreateSessionCookieValue(RegisteredServer server)
    {
        var payload = new WebSessionPayload(server.Id, server.ApiKey, DateTimeOffset.UtcNow.Add(SessionLifetime));
        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    public async Task<RegisteredServer?> AuthenticateCookieAsync(HttpRequest request, bool asTracking = false)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var cookieValue) || string.IsNullOrWhiteSpace(cookieValue))
            return null;

        WebSessionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebSessionPayload>(_protector.Unprotect(cookieValue));
        }
        catch
        {
            return null;
        }

        if (payload is null || payload.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        var query = asTracking ? _db.Servers.AsTracking() : _db.Servers.AsNoTracking();
        return await query
            .FirstOrDefaultAsync(s => s.Id == payload.ServerId && s.ApiKey == payload.ApiKey)
            .ConfigureAwait(false);
    }

    public static void DeleteSessionCookie(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
    }

    private sealed record WebSessionPayload(Guid ServerId, string ApiKey, DateTimeOffset ExpiresAt);
}

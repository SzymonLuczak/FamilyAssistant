using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyAssistant.Core.Google;

public sealed class GoogleAuthorization(IConfiguration config, GoogleState store, IHttpClientFactory clients, TimeProvider clock)
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    private readonly SemaphoreSlim tokensGate = new(1, 1);
    private readonly object pendingGate = new();
    private readonly Dictionary<string, (string Verifier, DateTimeOffset Expires)> pending = new();
    public bool Configured => File.Exists(config["Google:CredentialsPath"] ?? "/app/config-secrets/google-oauth.json");
    public string RedirectUri => config["Google:RedirectUri"] ?? "http://localhost:8080/google/callback";

    private async Task<(string Id, string Secret)> Credentials()
    {
        if (!Configured) throw new GoogleFailure("not_configured", 409);
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(config["Google:CredentialsPath"] ?? "/app/config-secrets/google-oauth.json"));
            var web = doc.RootElement.GetProperty("web");
            return (web.GetProperty("client_id").GetString()!, web.GetProperty("client_secret").GetString()!);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new GoogleFailure("invalid_client_configuration", 409); }
    }

    public async Task<string> Begin(HttpContext context)
    {
        var credentials = await Credentials();
        var redirect = new Uri(RedirectUri);
        if (!redirect.IsLoopback || redirect.Scheme != "http") throw new GoogleFailure("invalid_redirect_configuration", 409);
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (pendingGate)
        {
            foreach (var expired in pending.Where(p => p.Value.Expires <= clock.GetUtcNow()).Select(p => p.Key).ToArray()) pending.Remove(expired);
            if (pending.Count >= 20) throw new GoogleFailure("too_many_login_attempts", 429);
            pending[state] = (verifier, clock.GetUtcNow().AddMinutes(10));
        }
        context.Response.Cookies.Append("family-google-state", state, new CookieOptions
        { HttpOnly = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/google/callback", IsEssential = true });
        return QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?>
        {
            ["client_id"] = credentials.Id, ["redirect_uri"] = RedirectUri, ["response_type"] = "code",
            ["scope"] = Scope, ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = state,
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256"
        });
    }

    public async Task Complete(HttpContext context, string? state, string? code, string? error)
    {
        if (string.IsNullOrEmpty(state) || context.Request.Cookies["family-google-state"] != state)
            throw new GoogleFailure("invalid_oauth_state", 400);
        (string Verifier, DateTimeOffset Expires) attempt;
        lock (pendingGate)
        {
            if (!pending.Remove(state, out attempt) || attempt.Expires <= clock.GetUtcNow())
                throw new GoogleFailure("expired_oauth_state", 400);
        }
        context.Response.Cookies.Delete("family-google-state", new CookieOptions { Path = "/google/callback" });
        if (error != null) throw new GoogleFailure("consent_denied", 400);
        if (string.IsNullOrWhiteSpace(code)) throw new GoogleFailure("missing_oauth_code", 400);
        var credentials = await Credentials();
        await tokensGate.WaitAsync();
        try
        {
            var result = await Exchange(new Dictionary<string, string>
            {
                ["client_id"] = credentials.Id, ["client_secret"] = credentials.Secret,
                ["code"] = code, ["code_verifier"] = attempt.Verifier,
                ["redirect_uri"] = RedirectUri, ["grant_type"] = "authorization_code"
            });
            // Never combine the refresh token of a previous account with a new consent.
            var tokens = ParseTokens(result, null);
            await store.Write("calendars.json", Array.Empty<string>());
            await store.Write("tokens.json", tokens);
        }
        finally { tokensGate.Release(); }
    }

    public async Task<string> AccessToken(bool forceRefresh = false)
    {
        await tokensGate.WaitAsync();
        try
        {
            var tokens = await store.Read<GoogleTokens>("tokens.json") ?? throw new GoogleFailure("not_connected", 409);
            if (!forceRefresh && tokens.ExpiresAt > clock.GetUtcNow().AddMinutes(1)) return tokens.AccessToken;
            var credentials = await Credentials();
            var response = await Exchange(new Dictionary<string, string>
            {
                ["client_id"] = credentials.Id, ["client_secret"] = credentials.Secret,
                ["refresh_token"] = tokens.RefreshToken, ["grant_type"] = "refresh_token"
            });
            var refreshed = ParseTokens(response, tokens.RefreshToken);
            await store.Write("tokens.json", refreshed);
            return refreshed.AccessToken;
        }
        finally { tokensGate.Release(); }
    }

    private async Task<JsonElement> Exchange(Dictionary<string, string> form)
    {
        try
        {
            using var response = await clients.CreateClient("google").PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
            if (!response.IsSuccessStatusCode) throw new GoogleFailure(response.StatusCode == System.Net.HttpStatusCode.BadRequest ? "reauthorization_required" : "oauth_unavailable");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { throw new GoogleFailure("oauth_unavailable"); }
    }

    private GoogleTokens ParseTokens(JsonElement json, string? priorRefresh)
    {
        if (json.TryGetProperty("scope", out var scope) && !(scope.GetString() ?? "").Split(' ').Contains(Scope))
            throw new GoogleFailure("calendar_permission_missing", 403);
        var refresh = json.TryGetProperty("refresh_token", out var token) ? token.GetString() : priorRefresh;
        if (string.IsNullOrWhiteSpace(refresh)) throw new GoogleFailure("refresh_token_missing", 409);
        return new GoogleTokens(json.GetProperty("access_token").GetString()!, refresh,
            clock.GetUtcNow().AddSeconds(json.GetProperty("expires_in").GetInt32()));
    }

    public async Task<string> Status() => !Configured ? "not_configured" :
        await store.Read<GoogleTokens>("tokens.json") == null ? "not_connected" : "authorized";
}

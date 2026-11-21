namespace Poe1.Api;

/// <summary>
/// The server listens on 127.0.0.1 only, but a web page in the browser could still try to call it.
/// Reject requests whose Host is not localhost (DNS rebinding) or whose Origin is another site (CSRF).
/// </summary>
public sealed class LocalOnlyMiddleware(RequestDelegate next)
{
    private static bool IsLocal(string host) =>
        host is "127.0.0.1" or "localhost" or "[::1]";

    public Task InvokeAsync(HttpContext ctx)
    {
        if (!IsLocal(ctx.Request.Host.Host))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        var origin = ctx.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && (!Uri.TryCreate(origin, UriKind.Absolute, out var o) || !IsLocal(o.Host)))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return next(ctx);
    }
}

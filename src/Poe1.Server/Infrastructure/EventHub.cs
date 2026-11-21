using System.Text.Json;
using System.Threading.Channels;

namespace Poe1.Infrastructure;

/// <summary>Fan-out of server events to every open browser tab (Server-Sent Events).</summary>
public sealed class EventHub
{
    private readonly object _lock = new();
    private readonly List<Channel<string>> _subscribers = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Publish(string type, object payload)
    {
        var data = JsonSerializer.Serialize(new { type, payload }, Json);
        lock (_lock)
        {
            foreach (var ch in _subscribers) ch.Writer.TryWrite(data);
        }
    }

    public async Task StreamAsync(HttpContext ctx, CancellationToken ct)
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_lock) _subscribers.Add(ch);
        try
        {
            await ctx.Response.WriteAsync(": connected\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            using var keepAlive = new PeriodicTimer(TimeSpan.FromSeconds(20));
            var readTask = ch.Reader.WaitToReadAsync(ct).AsTask();
            var tickTask = keepAlive.WaitForNextTickAsync(ct).AsTask();
            while (!ct.IsCancellationRequested)
            {
                var done = await Task.WhenAny(readTask, tickTask);
                if (done == tickTask)
                {
                    await ctx.Response.WriteAsync(": ping\n\n", ct);
                    tickTask = keepAlive.WaitForNextTickAsync(ct).AsTask();
                }
                else
                {
                    if (!await readTask) break;
                    while (ch.Reader.TryRead(out var msg))
                        await ctx.Response.WriteAsync($"data: {msg}\n\n", ct);
                    readTask = ch.Reader.WaitToReadAsync(ct).AsTask();
                }
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_lock) _subscribers.Remove(ch);
        }
    }
}

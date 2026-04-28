using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Hosts a minimal localhost-only HTTP server for WebRTC streaming playback.
///     Jellyfin can play a <c>http://127.0.0.1:{port}/stream/{token}</c> URL directly.
///     Each token maps to a <see cref="PipeReader"/> whose data is piped into the HTTP response.
///     The server auto-cleans up a stream registration when the pipe completes.
/// </summary>
public sealed class LocalStreamEndpoint : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, PipeReader> _streams = new();
    private readonly ILogger<LocalStreamEndpoint> _logger;
    private WebApplication? _app;
    private int _port;

    public LocalStreamEndpoint(ILogger<LocalStreamEndpoint> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Starts the Kestrel listener on an OS-assigned port (first call only).
    ///     Subsequent calls are no-ops.
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateSlimBuilder();
        // Listen on IPv4 loopback only; ListenLocalhost(0) is not supported by Kestrel.
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); // port 0 = OS assigns
        builder.Logging.ClearProviders(); // use Jellyfin's logging, not a second pipeline

        _app = builder.Build();
        _app.MapGet("/stream/{token:guid}", ServeStreamAsync);

        await _app.StartAsync(ct).ConfigureAwait(false);

        // Resolve the OS-assigned port
        var addresses = _app.Urls;
        foreach (var addr in addresses)
        {
            if (Uri.TryCreate(addr, UriKind.Absolute, out var uri))
            {
                _port = uri.Port;
                break;
            }
        }

        _logger.LogInformation("LocalStreamEndpoint listening on http://127.0.0.1:{Port}", _port);
    }

    /// <summary>
    ///     Registers a <see cref="PipeReader"/> for the given token and returns the URL
    ///     Jellyfin should play.
    /// </summary>
    public async Task<string> RegisterStreamAsync(Guid token, PipeReader source, CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct).ConfigureAwait(false);
        _streams[token] = source;
        return $"http://127.0.0.1:{_port}/stream/{token}";
    }

    /// <summary>
    ///     Returns the streaming URL for a registered token without starting a stream.
    ///     Returns null if the token is not registered.
    /// </summary>
    public string? GetStreamUrl(Guid token) =>
        _streams.ContainsKey(token) ? $"http://127.0.0.1:{_port}/stream/{token}" : null;

    private async Task ServeStreamAsync(HttpContext context, Guid token)
    {
        if (!_streams.TryGetValue(token, out var reader))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/octet-stream";

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(context.RequestAborted).ConfigureAwait(false);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                    await context.Response.Body.WriteAsync(segment, context.RequestAborted).ConfigureAwait(false);

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted || result.IsCanceled) break;
            }

            await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Streaming client disconnected for token {Token}", token);
        }
        finally
        {
            _streams.TryRemove(token, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }
}

using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.SignalR;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JellyFederation.Plugin.Services;

/// <summary>
/// Transfers files over the UDP-hole-punched socket.
///
/// Protocol (simple length-prefixed framing over UDP bursts):
///   Sender → Receiver:
///     1. HEADER frame: JSON { FileName, FileSize }
///     2. DATA frames: [4-byte seq][chunk bytes]
///     3. EOF frame: magic bytes
///   Receiver → Sender:
///     ACK frames: [4-byte seq] (selective ACK, retransmit on timeout)
///
/// Note: for large files a production implementation would use QUIC
/// (System.Net.Quic). This implementation uses a simple stop-and-wait ARQ
/// over the punched UDP path so that no additional libraries are required.
/// </summary>
public class FileTransferService(
    ILibraryManager libraryManager,
    ILibraryMonitor libraryMonitor,
    HttpClient http,
    IPluginConfigurationProvider configProvider,
    ILogger<FileTransferService> logger)
{
    private const int ChunkSize = 32 * 1024; // 32 KB
    private const int AckTimeoutMs = 2_000;
    private const int MaxRetries = 10;
    private static readonly byte[] EofMagic = "JFEOF"u8.ToArray();

    // Active transfer cancellation tokens keyed by fileRequestId
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource>
        _activeCts = new();

    public void Cancel(Guid fileRequestId)
    {
        if (_activeCts.TryRemove(fileRequestId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public async Task SendFileAsync(
        Guid fileRequestId,
        string jellyfinItemId,
        Socket socket,
        IPEndPoint remoteEp,
        PluginConfiguration config)
    {
        if (!Guid.TryParse(jellyfinItemId, out var itemGuid))
        {
            logger.LogWarning("Invalid Jellyfin item ID format: {Id}", jellyfinItemId);
            return;
        }

        var item = libraryManager.GetItemById(itemGuid);
        if (item?.Path is null)
        {
            logger.LogError("Item {Id} not found or has no file path", jellyfinItemId);
            return;
        }

        var filePath = item.Path;
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            logger.LogError("File not found: {Path}", filePath);
            return;
        }

        logger.LogInformation("Sending {File} ({Size} bytes) to {Remote}",
            fileInfo.Name, fileInfo.Length, remoteEp);

        using var cts = new CancellationTokenSource();
        _activeCts[fileRequestId] = cts;
        try
        {
            var ct = cts.Token;

            // Send header
            var header = JsonSerializer.SerializeToUtf8Bytes(new
            {
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length
            });
            var headerFrame = BuildFrame(0xFFFF_FFFE, header);
            await SendWithAckAsync(socket, remoteEp, headerFrame, 0xFFFF_FFFE, ct);

            // Send data chunks
            await using var fs = File.OpenRead(filePath);
            var buffer = new byte[ChunkSize];
            uint seq = 0;
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = buffer[..bytesRead];
                var frame = BuildFrame(seq, chunk);
                await SendWithAckAsync(socket, remoteEp, frame, seq, ct);
                seq++;
            }

            // Send EOF
            await socket.SendToAsync(EofMagic, SocketFlags.None, remoteEp);
            logger.LogInformation("File sent successfully: {File}", fileInfo.Name);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Send cancelled for request {Id}", fileRequestId);
        }
        finally
        {
            _activeCts.TryRemove(fileRequestId, out _);
            socket.Dispose();
        }
    }

    public async Task ReceiveFileAsync(
        Guid fileRequestId,
        Socket socket,
        IPEndPoint remoteEp,
        PluginConfiguration config,
        HubConnection connection)
    {
        if (string.IsNullOrEmpty(config.DownloadDirectory))
        {
            logger.LogError("Download directory not configured");
            return;
        }

        Directory.CreateDirectory(config.DownloadDirectory);

        var recvBuffer = new byte[ChunkSize + 8];
        string? filePath = null;
        FileStream? fs = null;
        uint expectedSeq = 0;
        long totalBytes = 0;
        long bytesReceived = 0;
        var lastProgressReport = DateTime.UtcNow;

        using var transferCts = new CancellationTokenSource();
        _activeCts[fileRequestId] = transferCts;

        using var timeoutCts = new CancellationTokenSource();

        try
        {
            while (true)
            {
                // Reset the timeout for each packet instead of allocating a new CTS
                timeoutCts.CancelAfter(30_000);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    transferCts.Token, timeoutCts.Token);
                int received;
                try
                {
                    received = await socket.ReceiveAsync(recvBuffer, SocketFlags.None, linkedCts.Token);
                }
                catch (OperationCanceledException) when (transferCts.IsCancellationRequested)
                {
                    logger.LogInformation("Receive cancelled for request {Id}", fileRequestId);
                    break;
                }
                catch (OperationCanceledException)
                {
                    logger.LogError("Receive timed out waiting for data");
                    break;
                }

                var data = recvBuffer[..received];

                // Check for EOF
                if (data.Length == EofMagic.Length && data.SequenceEqual(EofMagic))
                {
                    logger.LogInformation("Received EOF — file transfer complete");
                    if (totalBytes > 0)
                        await ReportProgressAsync(connection, fileRequestId, totalBytes, totalBytes);
                    break;
                }

                if (data.Length < 4) continue;

                var seq = BitConverter.ToUInt32(data, 0);
                var payload = data[4..];

                // Header frame
                if (seq == 0xFFFF_FFFE)
                {
                    var header = JsonSerializer.Deserialize<FileHeader>(payload);
                    if (header is null) break;

                    filePath = Path.Combine(config.DownloadDirectory, header.FileName);
                    fs = File.Create(filePath);
                    totalBytes = header.FileSize;
                    logger.LogInformation("Receiving {File} ({Size} bytes)", header.FileName, header.FileSize);
                }
                else if (seq == expectedSeq && fs is not null)
                {
                    await fs.WriteAsync(payload);
                    bytesReceived += payload.Length;
                    expectedSeq++;

                    // Report progress at most once per second
                    if (totalBytes > 0 && (DateTime.UtcNow - lastProgressReport).TotalSeconds >= 1)
                    {
                        lastProgressReport = DateTime.UtcNow;
                        await ReportProgressAsync(connection, fileRequestId, bytesReceived, totalBytes);
                    }
                }

                // Send ACK
                await socket.SendToAsync(BitConverter.GetBytes(seq), SocketFlags.None, remoteEp);
            }
        }
        finally
        {
            _activeCts.TryRemove(fileRequestId, out _);
            if (fs is not null)
            {
                await fs.FlushAsync();
                await fs.DisposeAsync();
            }
            socket.Dispose();
        }

        if (filePath is not null && File.Exists(filePath))
        {
            logger.LogInformation("File saved to {Path}", filePath);
            TriggerLibraryScan(filePath, config.DownloadDirectory);

            // Mark the file request as completed on the federation server
            var cfg = configProvider.GetConfiguration();
            var req = new HttpRequestMessage(
                HttpMethod.Put,
                $"{cfg.FederationServerUrl.TrimEnd('/')}/api/filerequests/{fileRequestId}/complete");
            req.Headers.Add("X-Api-Key", cfg.ApiKey);
            try
            {
                var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    logger.LogWarning("Failed to mark request {Id} complete: {Status}", fileRequestId, resp.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify server of completion for request {Id}", fileRequestId);
            }
        }
    }

    private void TriggerLibraryScan(string filePath, string downloadDirectory)
    {
        // Check if the download directory is within any configured Jellyfin library.
        // If yes: a targeted ReportFileSystemChanged is enough.
        // If no: fall back to a full ValidateMediaLibrary and warn the user.
        var virtualFolders = libraryManager.GetVirtualFolders();
        bool isWatched = virtualFolders.Any(vf =>
            vf.Locations.Any(loc =>
                downloadDirectory.StartsWith(loc, StringComparison.OrdinalIgnoreCase) ||
                loc.StartsWith(downloadDirectory, StringComparison.OrdinalIgnoreCase)));

        if (isWatched)
        {
            libraryMonitor.ReportFileSystemChanged(filePath);
            logger.LogInformation("Triggered library scan for {File}", filePath);
        }
        else
        {
            logger.LogWarning(
                "Download directory {Dir} is not part of any Jellyfin library. " +
                "Add it via Dashboard → Libraries so downloaded files appear automatically. " +
                "Running a full library scan now as fallback.",
                downloadDirectory);
            _ = Task.Run(() => libraryManager.ValidateMediaLibrary(
                new Progress<double>(), CancellationToken.None));
        }
    }

    private async Task ReportProgressAsync(
        HubConnection connection, Guid fileRequestId, long bytesReceived, long totalBytes)
    {
        try
        {
            await connection.SendAsync("ReportTransferProgress",
                new TransferProgress(fileRequestId, bytesReceived, totalBytes));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to report transfer progress for {Id}", fileRequestId);
        }
    }

    private static byte[] BuildFrame(uint seq, byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), seq);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private async Task SendWithAckAsync(
        Socket socket, IPEndPoint remoteEp, byte[] frame, uint seq,
        CancellationToken ct = default)
    {
        var ackBuffer = new byte[4];
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await socket.SendToAsync(frame, SocketFlags.None, remoteEp);

            using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct, new CancellationTokenSource(AckTimeoutMs).Token);
            try
            {
                await socket.ReceiveAsync(ackBuffer, SocketFlags.None, ackCts.Token);
                if (BitConverter.ToUInt32(ackBuffer, 0) == seq)
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // propagate cancellation
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("ACK timeout for seq {Seq}, retrying ({Attempt}/{Max})",
                    seq, attempt + 1, MaxRetries);
            }
        }

        throw new IOException($"Failed to get ACK for seq {seq} after {MaxRetries} attempts");
    }

    private record FileHeader(string FileName, long FileSize);
}

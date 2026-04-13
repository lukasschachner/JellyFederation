using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Telemetry;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Models;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
/// </summary>
public partial class FileTransferService(
    ILibraryManager libraryManager,
    ILibraryMonitor libraryMonitor,
    HttpClient http,
    IPluginConfigurationProvider configProvider,
    ILogger<FileTransferService> logger)
{
    private const uint HeaderSequence = 0xFFFF_FFFE;
    private const int ChunkSize = 32 * 1024; // 32 KB
    private const int AckTimeoutMs = 2_000;
    private const int MaxRetries = 10;
    private const int QuicAcceptTimeoutMs = 10_000;
    private const long QuicDefaultStreamErrorCode = 0x0A;
    private const long QuicDefaultCloseErrorCode = 0x0B;
    private static readonly SslApplicationProtocol QuicAlpn = new("jellyfederation-transfer/1");
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
        PluginConfiguration config,
        TransferTransportMode selectedMode = TransferTransportMode.ArqUdp,
        TransferSelectionReason selectionReason = TransferSelectionReason.DefaultArq)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Producer);
        FederationTelemetry.SetCommonTags(activity, "file.transfer.send", "plugin", correlationId, releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight = FederationMetrics.BeginInflight("file.transfer.send", "plugin", FederationPlugin.ReleaseVersion);

        if (!Guid.TryParse(jellyfinItemId, out var itemGuid))
        {
            LogInvalidItemId(logger, jellyfinItemId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            return;
        }

        var item = libraryManager.GetItemById(itemGuid);
        if (item?.Path is null)
        {
            LogItemNotFound(logger, jellyfinItemId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            return;
        }

        var filePath = item.Path;
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            LogFileNotFound(logger, filePath);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            return;
        }

        var effectiveMode = selectedMode;
        var effectiveReason = selectionReason;
        if (selectedMode == TransferTransportMode.Quic &&
            (!config.PreferQuicForLargeFiles || !QuicConnection.IsSupported))
        {
            effectiveMode = TransferTransportMode.ArqUdp;
            effectiveReason = config.PreferQuicForLargeFiles
                ? TransferSelectionReason.QuicUnavailableLocal
                : TransferSelectionReason.QuicUnsupportedPeer;
            LogQuicFallbackBeforeSend(logger, fileRequestId, effectiveReason);
        }

        LogSendingFile(logger, fileInfo.Name, fileInfo.Length, remoteEp);
        LogTransferMode(logger, fileRequestId, effectiveMode, effectiveReason);

        using var cts = new CancellationTokenSource();
        _activeCts[fileRequestId] = cts;
        try
        {
            var ct = cts.Token;

            if (effectiveMode == TransferTransportMode.Quic)
            {
                try
                {
                    await SendWithQuicAsync(fileInfo, remoteEp, ct);
                    LogFileSent(logger, fileInfo.Name);
                    FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
                    FederationMetrics.RecordOperation("file.transfer.send.quic", "plugin", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    effectiveMode = TransferTransportMode.ArqUdp;
                    effectiveReason = TransferSelectionReason.FallbackAfterError;
                    LogQuicRuntimeFallback(logger, fileRequestId, ex.Message);
                    LogTransferMode(logger, fileRequestId, effectiveMode, effectiveReason);
                    FederationMetrics.RecordRetry("file.transfer.send.quic", "plugin", FederationPlugin.ReleaseVersion);
                }
            }

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
            LogFileSent(logger, fileInfo.Name);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
            FederationMetrics.RecordOperation("file.transfer.send.arq", "plugin", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
        }
        catch (OperationCanceledException)
        {
            LogSendCancelled(logger, fileRequestId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
        }
        catch (Exception ex)
        {
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, ex);
            var operation = effectiveMode == TransferTransportMode.Quic
                ? "file.transfer.send.quic"
                : "file.transfer.send.arq";
            FederationMetrics.RecordOperation(operation, "plugin", FederationTelemetry.OutcomeError, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
            throw;
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
        HubConnection connection,
        TransferTransportMode selectedMode = TransferTransportMode.ArqUdp,
        TransferSelectionReason selectionReason = TransferSelectionReason.DefaultArq)
    {
        var startedAt = Stopwatch.StartNew();
        var correlationId = FederationTelemetry.CreateCorrelationId();
        using var activity = FederationTelemetry.PluginActivitySource.StartActivity(
            FederationTelemetry.SpanFederationOperation,
            ActivityKind.Consumer);
        FederationTelemetry.SetCommonTags(activity, "file.transfer.receive", "plugin", correlationId, releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight = FederationMetrics.BeginInflight("file.transfer.receive", "plugin", FederationPlugin.ReleaseVersion);

        if (string.IsNullOrEmpty(config.DownloadDirectory))
        {
            LogDownloadDirNotConfigured(logger);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            return;
        }

        Directory.CreateDirectory(config.DownloadDirectory);

        var effectiveMode = selectedMode;
        var effectiveReason = selectionReason;
        if (selectedMode == TransferTransportMode.Quic &&
            (!config.PreferQuicForLargeFiles || !QuicConnection.IsSupported))
        {
            effectiveMode = TransferTransportMode.ArqUdp;
            effectiveReason = config.PreferQuicForLargeFiles
                ? TransferSelectionReason.QuicUnavailableLocal
                : TransferSelectionReason.QuicUnsupportedPeer;
            LogQuicFallbackBeforeReceive(logger, fileRequestId, effectiveReason);
        }

        LogTransferMode(logger, fileRequestId, effectiveMode, effectiveReason);

        var recvBuffer = new byte[ChunkSize + 8];
        string? filePath = null;
        FileStream? fs = null;
        uint expectedSeq = 0;
        bool headerReceived = false;
        bool receivedEof = false;
        long totalBytes = 0;
        long bytesReceived = 0;
        var lastProgressReport = DateTime.UtcNow;

        using var transferCts = new CancellationTokenSource();
        _activeCts[fileRequestId] = transferCts;

        using var timeoutCts = new CancellationTokenSource();

        if (effectiveMode == TransferTransportMode.Quic)
        {
            try
            {
                await ReceiveWithQuicAsync(fileRequestId, socket, config, connection, transferCts.Token, correlationId);
                _activeCts.TryRemove(fileRequestId, out _);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
                FederationMetrics.RecordOperation("file.transfer.receive.quic", "plugin", FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
                return;
            }
            catch (OperationCanceledException) when (transferCts.IsCancellationRequested)
            {
                LogReceiveCancelled(logger, fileRequestId);
                _activeCts.TryRemove(fileRequestId, out _);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
                return;
            }
            catch (Exception ex)
            {
                effectiveMode = TransferTransportMode.ArqUdp;
                effectiveReason = TransferSelectionReason.FallbackAfterError;
                LogQuicRuntimeFallback(logger, fileRequestId, ex.Message);
                LogTransferMode(logger, fileRequestId, effectiveMode, effectiveReason);
                FederationMetrics.RecordRetry("file.transfer.receive.quic", "plugin", FederationPlugin.ReleaseVersion);
            }
        }

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
                    LogReceiveCancelled(logger, fileRequestId);
                    break;
                }
                catch (OperationCanceledException)
                {
                    LogReceiveTimeout(logger);
                    FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeTimeout);
                    FederationMetrics.RecordTimeout("file.transfer.receive.arq", "plugin", FederationPlugin.ReleaseVersion);
                    break;
                }

                var data = recvBuffer[..received];

                // Check for EOF
                if (data.Length == EofMagic.Length && data.SequenceEqual(EofMagic))
                {
                    LogReceivedEof(logger);
                    if (totalBytes > 0)
                        await ReportProgressAsync(connection, fileRequestId, totalBytes, totalBytes);
                    receivedEof = true;
                    break;
                }

                if (data.Length < 4) continue;

                var seq = BitConverter.ToUInt32(data, 0);
                var payload = data[4..];

                // Header frame
                if (seq == HeaderSequence)
                {
                    if (!headerReceived)
                    {
                        var header = JsonSerializer.Deserialize<FileHeader>(payload);
                        if (header is null) break;

                        // Uniquify the destination path to avoid silently overwriting an existing file
                        filePath = GetUniqueFilePath(
                            Path.Combine(config.DownloadDirectory, header.FileName));
                        fs = File.Create(filePath);
                        totalBytes = header.FileSize;
                        headerReceived = true;
                        LogReceivingFile(logger, header.FileName, header.FileSize, filePath);
                    }
                    await socket.SendToAsync(BitConverter.GetBytes(HeaderSequence), SocketFlags.None, remoteEp);
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

                    await socket.SendToAsync(BitConverter.GetBytes(seq), SocketFlags.None, remoteEp);
                }
                else if (seq < expectedSeq)
                {
                    // Duplicate packet; ACK again so sender can move on if prior ACK was lost.
                    await socket.SendToAsync(BitConverter.GetBytes(seq), SocketFlags.None, remoteEp);
                }
                else
                {
                    // Out-of-order packet; skip ACK to force sender retransmit of missing sequence.
                    LogOutOfOrderPacket(logger, seq, expectedSeq);
                }
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

            // Delete partial file if transfer did not complete cleanly
            if (!receivedEof && filePath is not null && File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    LogDeletedIncompleteFile(logger, filePath);
                }
                catch (Exception ex)
                {
                    LogCouldNotDeleteIncompleteFile(logger, ex, filePath);
                }
            }

            socket.Dispose();
        }

        if (receivedEof && filePath is not null && File.Exists(filePath))
        {
            LogFileSaved(logger, filePath);
            TriggerLibraryScan(filePath, config.DownloadDirectory);

            // Mark the file request as completed on the federation server
            var cfg = configProvider.GetConfiguration();
            var req = new HttpRequestMessage(
                HttpMethod.Put,
                $"{cfg.FederationServerUrl.TrimEnd('/')}/api/filerequests/{fileRequestId}/complete");
            req.Headers.Add("X-Api-Key", cfg.ApiKey);
            TraceContextPropagation.InjectToHttpRequest(req);
            TraceContextPropagation.InjectCorrelationId(req.Headers, correlationId);
            try
            {
                var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    LogMarkCompleteFailed(logger, fileRequestId, resp.StatusCode);
            }
            catch (Exception ex)
            {
                LogNotifyCompletionFailed(logger, ex, fileRequestId);
            }
        }

        var outcome = receivedEof ? FederationTelemetry.OutcomeSuccess : FederationTelemetry.OutcomeCancelled;
        FederationTelemetry.SetOutcome(activity, outcome);
        FederationMetrics.RecordOperation("file.transfer.receive.arq", "plugin", outcome, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
    }

    private async Task SendWithQuicAsync(
        FileInfo fileInfo,
        IPEndPoint remoteEp,
        CancellationToken ct)
    {
        var connection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
        {
            RemoteEndPoint = remoteEp,
            DefaultStreamErrorCode = QuicDefaultStreamErrorCode,
            DefaultCloseErrorCode = QuicDefaultCloseErrorCode,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13,
                ApplicationProtocols = [QuicAlpn],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                TargetHost = "jellyfederation-transfer"
            }
        }, ct);

        await using (connection.ConfigureAwait(false))
        {
            var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct);
            await using (stream.ConfigureAwait(false))
            {
                var header = JsonSerializer.SerializeToUtf8Bytes(new FileHeader(fileInfo.Name, fileInfo.Length));
                await WriteInt32Async(stream, header.Length, ct);
                await stream.WriteAsync(header, ct);

                await using var fs = fileInfo.OpenRead();
                var buffer = new byte[ChunkSize];
                int bytesRead;
                while ((bytesRead = await fs.ReadAsync(buffer, ct)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                }

                stream.CompleteWrites();
            }
        }
    }

    private async Task ReceiveWithQuicAsync(
        Guid fileRequestId,
        Socket holePunchSocket,
        PluginConfiguration config,
        HubConnection connection,
        CancellationToken ct,
        string correlationId)
    {
        var localPort = ((IPEndPoint)holePunchSocket.LocalEndPoint!).Port;
        holePunchSocket.Dispose();

        string? filePath = null;
        bool completed = false;
        long totalBytes = 0;
        long bytesReceived = 0;

        try
        {
            var serverCert = CreateEphemeralCertificate();
            var listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Any, localPort),
                ApplicationProtocols = [QuicAlpn],
                ConnectionOptionsCallback = (_, _, _) =>
                {
                    var options = new QuicServerConnectionOptions
                    {
                        DefaultStreamErrorCode = QuicDefaultStreamErrorCode,
                        DefaultCloseErrorCode = QuicDefaultCloseErrorCode,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13,
                            ApplicationProtocols = [QuicAlpn],
                            ServerCertificate = serverCert
                        }
                    };
                    return ValueTask.FromResult(options);
                }
            });

            await using (listener.ConfigureAwait(false))
            {
                using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                acceptCts.CancelAfter(QuicAcceptTimeoutMs);
                var quicConn = await listener.AcceptConnectionAsync(acceptCts.Token);
                await using (quicConn.ConfigureAwait(false))
                {
                    var stream = await quicConn.AcceptInboundStreamAsync(ct);
                    await using (stream.ConfigureAwait(false))
                    {
                        var headerLength = await ReadInt32Async(stream, ct);
                        if (headerLength <= 0 || headerLength > 64 * 1024)
                            throw new IOException($"Invalid QUIC header length: {headerLength}");

                        var headerBytes = new byte[headerLength];
                        await ReadExactlyAsync(stream, headerBytes, ct);
                        var header = JsonSerializer.Deserialize<FileHeader>(headerBytes)
                            ?? throw new IOException("Invalid QUIC file header");

                        filePath = GetUniqueFilePath(Path.Combine(config.DownloadDirectory, header.FileName));
                        await using var fs = File.Create(filePath);
                        totalBytes = header.FileSize;
                        LogReceivingFile(logger, header.FileName, header.FileSize, filePath);

                        var buffer = new byte[ChunkSize];
                        var lastProgressReport = DateTime.UtcNow;
                        while (true)
                        {
                            var read = await stream.ReadAsync(buffer, ct);
                            if (read == 0)
                                break;

                            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                            bytesReceived += read;
                            if (totalBytes > 0 && (DateTime.UtcNow - lastProgressReport).TotalSeconds >= 1)
                            {
                                lastProgressReport = DateTime.UtcNow;
                                await ReportProgressAsync(connection, fileRequestId, bytesReceived, totalBytes);
                            }
                        }

                        await fs.FlushAsync(ct);
                    }
                }
            }

            if (totalBytes > 0)
                await ReportProgressAsync(connection, fileRequestId, totalBytes, totalBytes);
            completed = true;
        }
        finally
        {
            if (!completed && filePath is not null && File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    LogDeletedIncompleteFile(logger, filePath);
                }
                catch (Exception ex)
                {
                    LogCouldNotDeleteIncompleteFile(logger, ex, filePath);
                }
            }
        }

        if (completed && filePath is not null && File.Exists(filePath))
        {
            LogFileSaved(logger, filePath);
            TriggerLibraryScan(filePath, config.DownloadDirectory);

            var cfg = configProvider.GetConfiguration();
            var req = new HttpRequestMessage(
                HttpMethod.Put,
                $"{cfg.FederationServerUrl.TrimEnd('/')}/api/filerequests/{fileRequestId}/complete");
            req.Headers.Add("X-Api-Key", cfg.ApiKey);
            TraceContextPropagation.InjectToHttpRequest(req);
            TraceContextPropagation.InjectCorrelationId(req.Headers, correlationId);
            try
            {
                var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                    LogMarkCompleteFailed(logger, fileRequestId, resp.StatusCode);
            }
            catch (Exception ex)
            {
                LogNotifyCompletionFailed(logger, ex, fileRequestId);
            }
        }
    }

    private static X509Certificate2 CreateEphemeralCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=jellyfederation-transfer",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(12));
        return cert;
    }

    private static async Task WriteInt32Async(Stream stream, int value, CancellationToken ct)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        await stream.WriteAsync(bytes.ToArray(), ct);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken ct)
    {
        var bytes = new byte[4];
        await ReadExactlyAsync(stream, bytes, ct);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream");
            offset += read;
        }
    }

    /// <summary>
    /// Returns a unique file path by appending _1, _2, … if the file already exists.
    /// </summary>
    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{nameWithoutExt}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
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
            LogTriggeredLibraryScan(logger, filePath);
        }
        else
        {
            LogDownloadDirNotInLibrary(logger, downloadDirectory);
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
            LogReportProgressFailed(logger, ex, fileRequestId);
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

            // Use a single linked CTS; avoid the hidden inner CTS leak from
            // CreateLinkedTokenSource(ct, new CancellationTokenSource(timeout).Token)
            using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ackCts.CancelAfter(AckTimeoutMs);
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
                LogAckTimeout(logger, seq, attempt + 1, MaxRetries);
                FederationMetrics.RecordRetry("file.transfer.send", "plugin", FederationPlugin.ReleaseVersion);
            }
        }

        throw new IOException($"Failed to get ACK for seq {seq} after {MaxRetries} attempts");
    }

    private record FileHeader(string FileName, long FileSize);
}

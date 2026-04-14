using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using JellyFederation.Plugin.Configuration;
using JellyFederation.Shared.Models;
using JellyFederation.Shared.SignalR;
using JellyFederation.Shared.Telemetry;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace JellyFederation.Plugin.Services;

/// <summary>
///     Transfers files over the UDP-hole-punched socket.
///     Protocol (simple length-prefixed framing over UDP bursts):
///     Sender → Receiver:
///     1. HEADER frame: JSON { FileName, FileSize }
///     2. DATA frames: [4-byte seq][chunk bytes]
///     3. EOF frame: magic bytes
///     Receiver → Sender:
///     ACK frames: [4-byte seq] (selective ACK, retransmit on timeout)
/// </summary>
public partial class FileTransferService
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
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource>
        _activeCts = new();

    private readonly IPluginConfigurationProvider _configProvider;
    private readonly HttpClient _http;

    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<FileTransferService> _logger;

    /// <summary>
    ///     Transfers files over the UDP-hole-punched socket.
    ///     Protocol (simple length-prefixed framing over UDP bursts):
    ///     Sender → Receiver:
    ///     1. HEADER frame: JSON { FileName, FileSize }
    ///     2. DATA frames: [4-byte seq][chunk bytes]
    ///     3. EOF frame: magic bytes
    ///     Receiver → Sender:
    ///     ACK frames: [4-byte seq] (selective ACK, retransmit on timeout)
    /// </summary>
    public FileTransferService(ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        HttpClient http,
        IPluginConfigurationProvider configProvider,
        ILogger<FileTransferService> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _http = http;
        _configProvider = configProvider;
        _logger = logger;
    }

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
        FederationTelemetry.SetCommonTags(activity, "file.transfer.send", "plugin", correlationId,
            releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight =
            FederationMetrics.BeginInflight("file.transfer.send", "plugin", FederationPlugin.ReleaseVersion);

        var fileResolution = ResolveSourceFile(jellyfinItemId, correlationId);
        if (fileResolution.IsFailure)
        {
            var failure = fileResolution.Failure!;
            LogOperationFailureDescriptor(_logger, fileRequestId, failure.Code, failure.Category.ToString(), failure.Message);
            FederationTelemetry.SetFailure(activity, failure);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file.transfer.send", "plugin", FederationTelemetry.OutcomeError,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion, failure.Category.ToString(), failure.Code);
            return;
        }

        var fileInfo = fileResolution.RequireValue();
        var filePath = fileInfo.FullName;

        var effectiveMode = selectedMode;
        var effectiveReason = selectionReason;
        if (selectedMode == TransferTransportMode.Quic &&
            (!config.PreferQuicForLargeFiles || !QuicConnection.IsSupported))
        {
            effectiveMode = TransferTransportMode.ArqUdp;
            effectiveReason = config.PreferQuicForLargeFiles
                ? TransferSelectionReason.QuicUnavailableLocal
                : TransferSelectionReason.QuicUnsupportedPeer;
            LogQuicFallbackBeforeSend(_logger, fileRequestId, effectiveReason);
        }

        LogSendingFile(_logger, fileInfo.Name, fileInfo.Length, remoteEp);
        LogTransferMode(_logger, fileRequestId, effectiveMode, effectiveReason);

        using var cts = new CancellationTokenSource();
        _activeCts[fileRequestId] = cts;
        try
        {
            var ct = cts.Token;

            if (effectiveMode == TransferTransportMode.Quic)
                try
                {
                    await SendWithQuicAsync(fileInfo, remoteEp, ct).ConfigureAwait(false);
                    LogFileSent(_logger, fileInfo.Name);
                    FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
                    FederationMetrics.RecordOperation("file.transfer.send.quic", "plugin",
                        FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
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
                    LogQuicRuntimeFallback(_logger, fileRequestId, ex.Message);
                    LogTransferMode(_logger, fileRequestId, effectiveMode, effectiveReason);
                    FederationMetrics.RecordRetry("file.transfer.send.quic", "plugin", FederationPlugin.ReleaseVersion);
                }

            // Send header
            var header = JsonSerializer.SerializeToUtf8Bytes(new
            {
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length
            });
            var headerFrame = BuildFrame(0xFFFF_FFFE, header);
            await SendWithAckAsync(socket, remoteEp, headerFrame, 0xFFFF_FFFE, ct).ConfigureAwait(false);

            // Send data chunks
            var fs = File.OpenRead(filePath);
            await using var fs1 = fs.ConfigureAwait(false);
            var buffer = new byte[ChunkSize];
            uint seq = 0;
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = buffer[..bytesRead];
                var frame = BuildFrame(seq, chunk);
                await SendWithAckAsync(socket, remoteEp, frame, seq, ct).ConfigureAwait(false);
                seq++;
            }

            // Send EOF
            await socket.SendToAsync(EofMagic, SocketFlags.None, remoteEp).ConfigureAwait(false);
            LogFileSent(_logger, fileInfo.Name);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
            FederationMetrics.RecordOperation("file.transfer.send.arq", "plugin", FederationTelemetry.OutcomeSuccess,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion);
        }
        catch (OperationCanceledException)
        {
            LogSendCancelled(_logger, fileRequestId);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
        }
        catch (Exception ex)
        {
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError, ex);
            var operation = effectiveMode == TransferTransportMode.Quic
                ? "file.transfer.send.quic"
                : "file.transfer.send.arq";
            FederationMetrics.RecordOperation(operation, "plugin", FederationTelemetry.OutcomeError, startedAt.Elapsed,
                FederationPlugin.ReleaseVersion);
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
        FederationTelemetry.SetCommonTags(activity, "file.transfer.receive", "plugin", correlationId,
            releaseVersion: FederationPlugin.ReleaseVersion);
        using var inFlight =
            FederationMetrics.BeginInflight("file.transfer.receive", "plugin", FederationPlugin.ReleaseVersion);

        if (string.IsNullOrEmpty(config.DownloadDirectory))
        {
            LogDownloadDirNotConfigured(_logger);
            var failure = FailureDescriptor.Validation(
                "transfer.download_directory_missing",
                "Download directory is not configured.",
                correlationId);
            LogOperationFailureDescriptor(_logger, fileRequestId, failure.Code, failure.Category.ToString(), failure.Message);
            FederationTelemetry.SetFailure(activity, failure);
            FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeError);
            FederationMetrics.RecordOperation("file.transfer.receive", "plugin", FederationTelemetry.OutcomeError,
                startedAt.Elapsed, FederationPlugin.ReleaseVersion, failure.Category.ToString(), failure.Code);
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
            LogQuicFallbackBeforeReceive(_logger, fileRequestId, effectiveReason);
        }

        LogTransferMode(_logger, fileRequestId, effectiveMode, effectiveReason);

        var recvBuffer = new byte[ChunkSize + 8];
        string? filePath = null;
        FileStream? fs = null;
        uint expectedSeq = 0;
        var headerReceived = false;
        var receivedEof = false;
        long totalBytes = 0;
        long bytesReceived = 0;
        var lastProgressReport = DateTime.UtcNow;

        using var transferCts = new CancellationTokenSource();
        _activeCts[fileRequestId] = transferCts;

        using var timeoutCts = new CancellationTokenSource();

        if (effectiveMode == TransferTransportMode.Quic)
            try
            {
                await ReceiveWithQuicAsync(fileRequestId, socket, config, connection, transferCts.Token, correlationId)
                    .ConfigureAwait(false);
                _activeCts.TryRemove(fileRequestId, out _);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeSuccess);
                FederationMetrics.RecordOperation("file.transfer.receive.quic", "plugin",
                    FederationTelemetry.OutcomeSuccess, startedAt.Elapsed, FederationPlugin.ReleaseVersion);
                return;
            }
            catch (OperationCanceledException) when (transferCts.IsCancellationRequested)
            {
                LogReceiveCancelled(_logger, fileRequestId);
                _activeCts.TryRemove(fileRequestId, out _);
                FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeCancelled);
                return;
            }
            catch (Exception ex)
            {
                effectiveMode = TransferTransportMode.ArqUdp;
                effectiveReason = TransferSelectionReason.FallbackAfterError;
                LogQuicRuntimeFallback(_logger, fileRequestId, ex.Message);
                LogTransferMode(_logger, fileRequestId, effectiveMode, effectiveReason);
                FederationMetrics.RecordRetry("file.transfer.receive.quic", "plugin", FederationPlugin.ReleaseVersion);
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
                    received = await socket.ReceiveAsync(recvBuffer, SocketFlags.None, linkedCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (transferCts.IsCancellationRequested)
                {
                    LogReceiveCancelled(_logger, fileRequestId);
                    break;
                }
                catch (OperationCanceledException)
                {
                    LogReceiveTimeout(_logger);
                    FederationTelemetry.SetOutcome(activity, FederationTelemetry.OutcomeTimeout);
                    FederationMetrics.RecordTimeout("file.transfer.receive.arq", "plugin",
                        FederationPlugin.ReleaseVersion);
                    break;
                }

                var data = recvBuffer[..received];

                // Check for EOF
                if (data.Length == EofMagic.Length && data.SequenceEqual(EofMagic))
                {
                    LogReceivedEof(_logger);
                    if (totalBytes > 0)
                        await ReportProgressAsync(connection, fileRequestId, totalBytes, totalBytes)
                            .ConfigureAwait(false);
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
                        LogReceivingFile(_logger, header.FileName, header.FileSize, filePath);
                    }

                    await socket.SendToAsync(BitConverter.GetBytes(HeaderSequence), SocketFlags.None, remoteEp)
                        .ConfigureAwait(false);
                }
                else if (seq == expectedSeq && fs is not null)
                {
                    await fs.WriteAsync(payload).ConfigureAwait(false);
                    bytesReceived += payload.Length;
                    expectedSeq++;

                    // Report progress at most once per second
                    if (totalBytes > 0 && (DateTime.UtcNow - lastProgressReport).TotalSeconds >= 1)
                    {
                        lastProgressReport = DateTime.UtcNow;
                        await ReportProgressAsync(connection, fileRequestId, bytesReceived, totalBytes)
                            .ConfigureAwait(false);
                    }

                    await socket.SendToAsync(BitConverter.GetBytes(seq), SocketFlags.None, remoteEp)
                        .ConfigureAwait(false);
                }
                else if (seq < expectedSeq)
                {
                    // Duplicate packet; ACK again so sender can move on if prior ACK was lost.
                    await socket.SendToAsync(BitConverter.GetBytes(seq), SocketFlags.None, remoteEp)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Out-of-order packet; skip ACK to force sender retransmit of missing sequence.
                    LogOutOfOrderPacket(_logger, seq, expectedSeq);
                }
            }
        }
        finally
        {
            _activeCts.TryRemove(fileRequestId, out _);
            if (fs is not null)
            {
                await fs.FlushAsync().ConfigureAwait(false);
                await fs.DisposeAsync().ConfigureAwait(false);
            }

            // Delete partial file if transfer did not complete cleanly
            if (!receivedEof && filePath is not null && File.Exists(filePath))
                try
                {
                    File.Delete(filePath);
                    LogDeletedIncompleteFile(_logger, filePath);
                }
                catch (Exception ex)
                {
                    LogCouldNotDeleteIncompleteFile(_logger, ex, filePath);
                }

            socket.Dispose();
        }

        if (receivedEof && filePath is not null && File.Exists(filePath))
        {
            LogFileSaved(_logger, filePath);
            TriggerLibraryScan(filePath, config.DownloadDirectory);

            // Mark the file request as completed on the federation server
            var cfg = _configProvider.GetConfiguration();
            var req = new HttpRequestMessage(
                HttpMethod.Put,
                $"{cfg.FederationServerUrl.TrimEnd('/')}/api/filerequests/{fileRequestId}/complete");
            req.Headers.Add("X-Api-Key", cfg.ApiKey);
            TraceContextPropagation.InjectToHttpRequest(req);
            TraceContextPropagation.InjectCorrelationId(req.Headers, correlationId);
            try
            {
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    LogMarkCompleteFailed(_logger, fileRequestId, resp.StatusCode);
            }
            catch (Exception ex)
            {
                LogNotifyCompletionFailed(_logger, ex, fileRequestId);
            }
        }

        var outcome = receivedEof ? FederationTelemetry.OutcomeSuccess : FederationTelemetry.OutcomeCancelled;
        FederationTelemetry.SetOutcome(activity, outcome);
        FederationMetrics.RecordOperation("file.transfer.receive.arq", "plugin", outcome, startedAt.Elapsed,
            FederationPlugin.ReleaseVersion);
    }

    private OperationOutcome<FileInfo> ResolveSourceFile(string jellyfinItemId, string correlationId)
    {
        if (!Guid.TryParse(jellyfinItemId, out var itemGuid))
        {
            LogInvalidItemId(_logger, jellyfinItemId);
            return OperationOutcome<FileInfo>.Fail(FailureDescriptor.Validation(
                "transfer.item_id_invalid",
                $"Invalid Jellyfin item ID format: {jellyfinItemId}",
                correlationId));
        }

        var item = _libraryManager.GetItemById(itemGuid);
        if (item?.Path is null)
        {
            LogItemNotFound(_logger, jellyfinItemId);
            return OperationOutcome<FileInfo>.Fail(FailureDescriptor.NotFound(
                "transfer.item_not_found",
                $"Item {jellyfinItemId} not found or has no file path.",
                correlationId));
        }

        var fileInfo = new FileInfo(item.Path);
        if (!fileInfo.Exists)
        {
            LogFileNotFound(_logger, item.Path);
            return OperationOutcome<FileInfo>.Fail(FailureDescriptor.NotFound(
                "transfer.file_not_found",
                $"File not found: {item.Path}",
                correlationId));
        }

        return OperationOutcome<FileInfo>.Success(fileInfo);
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
                EnabledSslProtocols = SslProtocols.Tls13,
                ApplicationProtocols = [QuicAlpn],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                TargetHost = "jellyfederation-transfer"
            }
        }, ct).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct)
                .ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var header = JsonSerializer.SerializeToUtf8Bytes(new FileHeader(fileInfo.Name, fileInfo.Length));
                await WriteInt32Async(stream, header.Length, ct).ConfigureAwait(false);
                await stream.WriteAsync(header, ct).ConfigureAwait(false);

                var fs = fileInfo.OpenRead();
                await using var fs1 = fs.ConfigureAwait(false);
                var buffer = new byte[ChunkSize];
                int bytesRead;
                while ((bytesRead = await fs.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    await stream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

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
        var completed = false;
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
                            EnabledSslProtocols = SslProtocols.Tls13,
                            ApplicationProtocols = [QuicAlpn],
                            ServerCertificate = serverCert
                        }
                    };
                    return ValueTask.FromResult(options);
                }
            }).ConfigureAwait(false);

            await using (listener.ConfigureAwait(false))
            {
                using var acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                acceptCts.CancelAfter(QuicAcceptTimeoutMs);
                var quicConn = await listener.AcceptConnectionAsync(acceptCts.Token).ConfigureAwait(false);
                await using (quicConn.ConfigureAwait(false))
                {
                    var stream = await quicConn.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
                    await using (stream.ConfigureAwait(false))
                    {
                        var headerLength = await ReadInt32Async(stream, ct).ConfigureAwait(false);
                        if (headerLength <= 0 || headerLength > 64 * 1024)
                            throw new IOException($"Invalid QUIC header length: {headerLength}");

                        var headerBytes = new byte[headerLength];
                        await ReadExactlyAsync(stream, headerBytes, ct).ConfigureAwait(false);
                        var header = JsonSerializer.Deserialize<FileHeader>(headerBytes)
                                     ?? throw new IOException("Invalid QUIC file header");

                        filePath = GetUniqueFilePath(Path.Combine(config.DownloadDirectory, header.FileName));
                        var fs = File.Create(filePath);
                        await using var fs1 = fs.ConfigureAwait(false);
                        totalBytes = header.FileSize;
                        LogReceivingFile(_logger, header.FileName, header.FileSize, filePath);

                        var buffer = new byte[ChunkSize];
                        var lastProgressReport = DateTime.UtcNow;
                        while (true)
                        {
                            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                            if (read == 0)
                                break;

                            await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            bytesReceived += read;
                            if (totalBytes > 0 && (DateTime.UtcNow - lastProgressReport).TotalSeconds >= 1)
                            {
                                lastProgressReport = DateTime.UtcNow;
                                await ReportProgressAsync(connection, fileRequestId, bytesReceived, totalBytes)
                                    .ConfigureAwait(false);
                            }
                        }

                        await fs.FlushAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            if (totalBytes > 0)
                await ReportProgressAsync(connection, fileRequestId, totalBytes, totalBytes).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed && filePath is not null && File.Exists(filePath))
                try
                {
                    File.Delete(filePath);
                    LogDeletedIncompleteFile(_logger, filePath);
                }
                catch (Exception ex)
                {
                    LogCouldNotDeleteIncompleteFile(_logger, ex, filePath);
                }
        }

        if (completed && filePath is not null && File.Exists(filePath))
        {
            LogFileSaved(_logger, filePath);
            TriggerLibraryScan(filePath, config.DownloadDirectory);

            var cfg = _configProvider.GetConfiguration();
            var req = new HttpRequestMessage(
                HttpMethod.Put,
                $"{cfg.FederationServerUrl.TrimEnd('/')}/api/filerequests/{fileRequestId}/complete");
            req.Headers.Add("X-Api-Key", cfg.ApiKey);
            TraceContextPropagation.InjectToHttpRequest(req);
            TraceContextPropagation.InjectCorrelationId(req.Headers, correlationId);
            try
            {
                var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    LogMarkCompleteFailed(_logger, fileRequestId, resp.StatusCode);
            }
            catch (Exception ex)
            {
                LogNotifyCompletionFailed(_logger, ex, fileRequestId);
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
        await stream.WriteAsync(bytes.ToArray(), ct).ConfigureAwait(false);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken ct)
    {
        var bytes = new byte[4];
        await ReadExactlyAsync(stream, bytes, ct).ConfigureAwait(false);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream");
            offset += read;
        }
    }

    /// <summary>
    ///     Returns a unique file path by appending _1, _2, … if the file already exists.
    /// </summary>
    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 1;; i++)
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
        var virtualFolders = _libraryManager.GetVirtualFolders();
        var isWatched = virtualFolders.Any(vf =>
            vf.Locations.Any(loc =>
                downloadDirectory.StartsWith(loc, StringComparison.OrdinalIgnoreCase) ||
                loc.StartsWith(downloadDirectory, StringComparison.OrdinalIgnoreCase)));

        if (isWatched)
        {
            _libraryMonitor.ReportFileSystemChanged(filePath);
            LogTriggeredLibraryScan(_logger, filePath);
        }
        else
        {
            LogDownloadDirNotInLibrary(_logger, downloadDirectory);
            _ = Task.Run(() => _libraryManager.ValidateMediaLibrary(
                new Progress<double>(), CancellationToken.None));
        }
    }

    private async Task ReportProgressAsync(
        HubConnection connection, Guid fileRequestId, long bytesReceived, long totalBytes)
    {
        try
        {
            await connection.SendAsync("ReportTransferProgress",
                new TransferProgress(fileRequestId, bytesReceived, totalBytes)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogReportProgressFailed(_logger, ex, fileRequestId);
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
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await socket.SendToAsync(frame, SocketFlags.None, remoteEp).ConfigureAwait(false);

            // Use a single linked CTS; avoid the hidden inner CTS leak from
            // CreateLinkedTokenSource(ct, new CancellationTokenSource(timeout).Token)
            using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ackCts.CancelAfter(AckTimeoutMs);
            try
            {
                await socket.ReceiveAsync(ackBuffer, SocketFlags.None, ackCts.Token).ConfigureAwait(false);
                if (BitConverter.ToUInt32(ackBuffer, 0) == seq)
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // propagate cancellation
            }
            catch (OperationCanceledException)
            {
                LogAckTimeout(_logger, seq, attempt + 1, MaxRetries);
                FederationMetrics.RecordRetry("file.transfer.send", "plugin", FederationPlugin.ReleaseVersion);
            }
        }

        throw new IOException($"Failed to get ACK for seq {seq} after {MaxRetries} attempts");
    }

    private record FileHeader
    {
        public FileHeader(string FileName, long FileSize)
        {
            this.FileName = FileName;
            this.FileSize = FileSize;
        }

        public string FileName { get; init; }
        public long FileSize { get; init; }

        public void Deconstruct(out string FileName, out long FileSize)
        {
            FileName = this.FileName;
            FileSize = this.FileSize;
        }
    }
}

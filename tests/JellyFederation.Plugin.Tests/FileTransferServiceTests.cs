using System.Net;
using System.Net.Sockets;
using System.Reflection;
using JellyFederation.Plugin.Configuration;
using System.Text.Json;
using JellyFederation.Plugin.Services;
using JellyFederation.Shared.SignalR;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class FileTransferServiceTests
{
    private static readonly FieldInfo ActiveCtsField = typeof(FileTransferService)
        .GetField("_activeCts", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_activeCts field not found.");
    private static readonly MethodInfo WriteInt32AsyncMethod = GetStaticMethod("WriteInt32Async");
    private static readonly MethodInfo ReadInt32AsyncMethod = GetStaticMethod("ReadInt32Async");
    private static readonly MethodInfo ReadExactlyAsyncMethod = GetStaticMethod("ReadExactlyAsync");
    private static readonly MethodInfo CreateDataChannelFrameMethod = GetStaticMethod("CreateDataChannelFrame");
    private static readonly MethodInfo TryGetDataChannelPayloadMethod = GetStaticMethod("TryGetDataChannelPayload");
    private static readonly MethodInfo BuildFrameMethod = GetStaticMethod("BuildFrame");
    private static readonly MethodInfo CreateEphemeralCertificateMethod = GetStaticMethod("CreateEphemeralCertificate");
    private static readonly MethodInfo GetUniqueFilePathMethod = GetStaticMethod("GetUniqueFilePath");

    [Fact]
    public void Cancel_RemovesAndDisposesActiveCancellationSource()
    {
        var service = CreateService();
        var requestId = Guid.NewGuid();
        var dictionary = Assert.IsAssignableFrom<System.Collections.IDictionary>(ActiveCtsField.GetValue(service));
        var cts = new CancellationTokenSource();
        dictionary[requestId] = cts;

        service.Cancel(requestId);

        Assert.False(dictionary.Contains(requestId));
        Assert.True(cts.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => cts.Cancel());
    }

    [Fact]
    public async Task ReceiveFileAsync_ReturnsEarly_WhenDownloadDirectoryIsMissing()
    {
        var service = CreateService();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        await service.ReceiveFileAsync(
            Guid.NewGuid(),
            socket,
            new IPEndPoint(IPAddress.Loopback, 9),
            new PluginConfiguration { DownloadDirectory = string.Empty },
            connection: null!);
    }

    [Fact]
    public async Task ReceiveFileAsync_ArqUdp_WritesFileAcksFramesAndKeepsCompletedDownload()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-arq-receive-tests");
        try
        {
            var libraryManager = A.Fake<ILibraryManager>();
            A.CallTo(() => libraryManager.GetVirtualFolders())
                .Returns([new VirtualFolderInfo { Locations = [directory.FullName] }]);
            var libraryMonitor = A.Fake<ILibraryMonitor>();
            var service = CreateService(libraryManager, libraryMonitor);
            using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sender.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var receiverEndpoint = (IPEndPoint)receiver.LocalEndPoint!;
            var senderEndpoint = (IPEndPoint)sender.LocalEndPoint!;
            var receiveTask = service.ReceiveFileAsync(
                Guid.NewGuid(),
                receiver,
                senderEndpoint,
                new PluginConfiguration { DownloadDirectory = directory.FullName },
                connection: null!);

            await SendDatagramAndReceiveAckAsync(sender, receiverEndpoint, BuildFrameForTest(0xFFFF_FFFEu, JsonSerializer.SerializeToUtf8Bytes(new { FileName = "movie.bin", FileSize = 5L })), 0xFFFF_FFFEu);
            await SendDatagramAndReceiveAckAsync(sender, receiverEndpoint, BuildFrameForTest(0, [1, 2, 3]), 0);
            await SendDatagramAndReceiveAckAsync(sender, receiverEndpoint, BuildFrameForTest(0, [1, 2, 3]), 0);
            await SendDatagramAndReceiveAckAsync(sender, receiverEndpoint, BuildFrameForTest(1, [4, 5]), 1);
            await sender.SendToAsync("JFEOF"u8.ToArray(), SocketFlags.None, receiverEndpoint);

            await receiveTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var savedPath = Path.Combine(directory.FullName, "movie.bin");
            Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(savedPath));
            A.CallTo(() => libraryMonitor.ReportFileSystemChanged(savedPath)).MustHaveHappenedOnceExactly();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReceiveRelayAsync_DrainsPendingChunksWritesFileAndMarksComplete()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-relay-receive-tests");
        try
        {
            var libraryManager = A.Fake<ILibraryManager>();
            A.CallTo(() => libraryManager.GetVirtualFolders())
                .Returns([new VirtualFolderInfo { Locations = [directory.FullName] }]);
            var libraryMonitor = A.Fake<ILibraryMonitor>();
            var service = CreateService(libraryManager, libraryMonitor);
            var requestId = Guid.NewGuid();
            service.EnqueueRelayChunk(new RelayChunk(requestId, -1, false,
                JsonSerializer.SerializeToUtf8Bytes(new { FileName = "relay.bin", FileSize = 4L })));
            service.EnqueueRelayChunk(new RelayChunk(requestId, 0, false, [1, 2]));
            service.EnqueueRelayChunk(new RelayChunk(requestId, 1, false, [3, 4]));
            service.EnqueueRelayChunk(new RelayChunk(requestId, 2, true, []));

            await service.ReceiveRelayAsync(
                requestId,
                connection: null!,
                new PluginConfiguration { DownloadDirectory = directory.FullName },
                TestContext.Current.CancellationToken);

            var savedPath = Path.Combine(directory.FullName, "relay.bin");
            Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(savedPath));
            A.CallTo(() => libraryMonitor.ReportFileSystemChanged(savedPath)).MustHaveHappenedOnceExactly();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReceiveRelayAsync_WithoutHeader_UsesGeneratedFileName()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-relay-no-header-tests");
        try
        {
            var service = CreateService();
            var requestId = Guid.NewGuid();
            service.EnqueueRelayChunk(new RelayChunk(requestId, 0, false, [9, 8]));
            service.EnqueueRelayChunk(new RelayChunk(requestId, 1, true, []));

            await service.ReceiveRelayAsync(
                requestId,
                connection: null!,
                new PluginConfiguration { DownloadDirectory = directory.FullName },
                TestContext.Current.CancellationToken);

            var savedPath = Path.Combine(directory.FullName, $"relay-{requestId}");
            Assert.Equal([9, 8], File.ReadAllBytes(savedPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReceiveRelayAsync_ReturnsEarly_WhenDownloadDirectoryIsMissing()
    {
        var service = CreateService();

        await service.ReceiveRelayAsync(
            Guid.NewGuid(),
            connection: null!,
            new PluginConfiguration { DownloadDirectory = string.Empty },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendRelayAsync_ReturnsEarly_WhenJellyfinItemIdIsInvalid()
    {
        var service = CreateService();

        await service.SendRelayAsync(
            Guid.NewGuid(),
            "not-a-guid",
            connection: null!,
            new PluginConfiguration(),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendFileAsync_ArqUdp_SendsHeaderDataAndEofWithAcks()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-arq-send-tests");
        try
        {
            var itemId = Guid.NewGuid();
            var sourcePath = Path.Combine(directory.FullName, "source.bin");
            await File.WriteAllBytesAsync(sourcePath, [10, 20, 30, 40, 50], TestContext.Current.CancellationToken);
            var item = A.Fake<BaseItem>();
            A.CallTo(() => item.Path).Returns(sourcePath);
            var libraryManager = A.Fake<ILibraryManager>();
            A.CallTo(() => libraryManager.GetItemById(itemId)).Returns(item);
            var service = CreateService(libraryManager);
            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sender.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            peer.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var senderEndpoint = (IPEndPoint)sender.LocalEndPoint!;
            var peerEndpoint = (IPEndPoint)peer.LocalEndPoint!;
            var receivedFrames = new List<byte[]>();
            var peerTask = Task.Run(async () =>
            {
                var buffer = new byte[1024];
                while (true)
                {
                    var received = await peer.ReceiveAsync(buffer, SocketFlags.None, TestContext.Current.CancellationToken);
                    var data = buffer[..received].ToArray();
                    if (data.SequenceEqual("JFEOF"u8.ToArray()))
                        break;
                    receivedFrames.Add(data);
                    var sequence = BitConverter.ToUInt32(data.AsSpan(0, 4));
                    await peer.SendToAsync(BitConverter.GetBytes(sequence), SocketFlags.None, senderEndpoint);
                }
            }, TestContext.Current.CancellationToken);

            await service.SendFileAsync(
                Guid.NewGuid(),
                itemId.ToString(),
                sender,
                peerEndpoint,
                new PluginConfiguration());
            await peerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(2, receivedFrames.Count);
            Assert.Equal(0xFFFF_FFFEu, BitConverter.ToUInt32(receivedFrames[0].AsSpan(0, 4)));
            Assert.Equal(0u, BitConverter.ToUInt32(receivedFrames[1].AsSpan(0, 4)));
            Assert.Equal([10, 20, 30, 40, 50], receivedFrames[1][4..]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SendFileAsync_ReturnsEarly_WhenJellyfinItemIdIsInvalid()
    {
        var service = CreateService();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        await service.SendFileAsync(
            Guid.NewGuid(),
            "not-a-guid",
            socket,
            new IPEndPoint(IPAddress.Loopback, 9),
            new PluginConfiguration());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task Int32StreamHelpers_RoundTripBoundaryValues(int value)
    {
        await using var stream = new MemoryStream();

        await InvokeWriteInt32Async(stream, value);
        stream.Position = 0;

        var roundTrip = await InvokeReadInt32Async(stream);

        Assert.Equal(value, roundTrip);
    }

    [Fact]
    public async Task ReadInt32Async_ThrowsOnTruncatedPayload()
    {
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => InvokeReadInt32Async(stream));
    }

    [Fact]
    public async Task ReadExactlyAsync_ThrowsWhenStreamEndsEarly()
    {
        await using var stream = new MemoryStream([1, 2]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => InvokeReadExactlyAsync(stream, new byte[4]));
    }

    [Fact]
    public async Task ReadExactlyAsync_ReadsAcrossMultiplePartialReads()
    {
        await using var stream = new ChunkedReadStream([1, 2, 3, 4], maxChunkSize: 1);
        var buffer = new byte[4];

        await InvokeReadExactlyAsync(stream, buffer);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);
    }

    [Fact]
    public void GetUniqueFilePath_ReturnsOriginalPath_WhenFileDoesNotExist()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-file-transfer-tests");
        try
        {
            var originalPath = Path.Combine(directory.FullName, "movie.mkv");

            var unique = Assert.IsType<string>(GetUniqueFilePathMethod.Invoke(null, [originalPath]));

            Assert.Equal(originalPath, unique);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetUniqueFilePath_AppendsIncrement_WhenFileExists()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfederation-file-transfer-tests");
        try
        {
            var originalPath = Path.Combine(directory.FullName, "movie.mkv");
            File.WriteAllText(originalPath, "x");
            File.WriteAllText(Path.Combine(directory.FullName, "movie_1.mkv"), "x");

            var unique = Assert.IsType<string>(GetUniqueFilePathMethod.Invoke(null, [originalPath]));

            Assert.EndsWith("movie_2.mkv", unique, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildFrame_PrefixesLittleEndianSequenceAndPayload()
    {
        var frame = Assert.IsType<byte[]>(BuildFrameMethod.Invoke(null, [0x0102_0304u, new byte[] { 9, 8 }]));

        Assert.Equal([4, 3, 2, 1, 9, 8], frame);
    }

    [Fact]
    public void CreateEphemeralCertificate_ReturnsUsableShortLivedCertificate()
    {
        using var cert = Assert.IsType<System.Security.Cryptography.X509Certificates.X509Certificate2>(
            CreateEphemeralCertificateMethod.Invoke(null, []));

        Assert.Contains("CN=jellyfederation-transfer", cert.Subject, StringComparison.Ordinal);
        Assert.True(cert.HasPrivateKey);
        Assert.True(cert.NotAfter > DateTime.UtcNow);
    }

    [Fact]
    public void DataChannelFrameHelpers_HandleBoundaryAndUnexpectedKinds()
    {
        const byte headerFrame = 1;
        const byte dataFrame = 2;
        const byte endFrame = 3;

        var emptyPayloadFrame = InvokeCreateDataChannelFrame(endFrame, ReadOnlyMemory<byte>.Empty);
        Assert.Equal([endFrame], emptyPayloadFrame);

        var oneByteFrame = InvokeCreateDataChannelFrame(dataFrame, new byte[] { 42 });
        Assert.Equal(dataFrame, oneByteFrame[0]);
        Assert.Equal(42, oneByteFrame[1]);

        object?[] wrongKindArgs = [oneByteFrame, headerFrame, null];
        var wrongKind = Assert.IsType<bool>(TryGetDataChannelPayloadMethod.Invoke(null, wrongKindArgs));
        Assert.False(wrongKind);

        object?[] eofArgs = [emptyPayloadFrame, endFrame, null];
        var eofParsed = Assert.IsType<bool>(TryGetDataChannelPayloadMethod.Invoke(null, eofArgs));
        Assert.True(eofParsed);
        var eofPayload = Assert.IsType<ReadOnlyMemory<byte>>(eofArgs[2]);
        Assert.Equal(0, eofPayload.Length);

        object?[] dataArgs = [oneByteFrame, dataFrame, null];
        var dataParsed = Assert.IsType<bool>(TryGetDataChannelPayloadMethod.Invoke(null, dataArgs));
        Assert.True(dataParsed);
        var payload = Assert.IsType<ReadOnlyMemory<byte>>(dataArgs[2]);
        Assert.Equal(42, payload.Span[0]);

        object?[] emptyFrameArgs = [Array.Empty<byte>(), dataFrame, null];
        var emptyFrameParsed = Assert.IsType<bool>(TryGetDataChannelPayloadMethod.Invoke(null, emptyFrameArgs));
        Assert.False(emptyFrameParsed);
    }

    private sealed class TestConfigurationProvider : IPluginConfigurationProvider
    {
        public PluginConfiguration GetConfiguration() => new()
        {
            FederationServerUrl = "http://127.0.0.1",
            ApiKey = "test-key"
        };
    }

    private sealed class ChunkedReadStream : MemoryStream
    {
        private readonly int _maxChunkSize;

        public ChunkedReadStream(byte[] buffer, int maxChunkSize)
            : base(buffer)
        {
            _maxChunkSize = maxChunkSize;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            var bounded = destination.Length > _maxChunkSize
                ? destination[.._maxChunkSize]
                : destination;
            return base.ReadAsync(bounded, cancellationToken);
        }
    }

    private static FileTransferService CreateService(
        ILibraryManager? libraryManager = null,
        ILibraryMonitor? libraryMonitor = null)
    {
        if (libraryManager is null)
        {
            libraryManager = A.Fake<ILibraryManager>();
            A.CallTo(() => libraryManager.GetVirtualFolders()).Returns([]);
        }

        return new FileTransferService(
            libraryManager: libraryManager,
            libraryMonitor: libraryMonitor ?? A.Fake<ILibraryMonitor>(),
            http: new HttpClient(),
            configProvider: new TestConfigurationProvider(),
            logger: NullLogger<FileTransferService>.Instance);
    }

    private static byte[] BuildFrameForTest(uint sequence, byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), sequence);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static async Task SendDatagramAndReceiveAckAsync(Socket sender, EndPoint receiverEndpoint, byte[] frame, uint expectedAck)
    {
        await sender.SendToAsync(frame, SocketFlags.None, receiverEndpoint);
        var ack = new byte[4];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = await sender.ReceiveAsync(ack, SocketFlags.None, cts.Token);
        Assert.Equal(4, received);
        Assert.Equal(expectedAck, BitConverter.ToUInt32(ack));
    }

    private static MethodInfo GetStaticMethod(string methodName) =>
        typeof(FileTransferService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Method '{methodName}' not found.");

    private static async Task InvokeWriteInt32Async(Stream stream, int value)
    {
        var task = Assert.IsAssignableFrom<Task>(WriteInt32AsyncMethod.Invoke(null, [stream, value, CancellationToken.None]));
        await task;
    }

    private static async Task<int> InvokeReadInt32Async(Stream stream)
    {
        var task = Assert.IsType<Task<int>>(ReadInt32AsyncMethod.Invoke(null, [stream, CancellationToken.None]));
        return await task;
    }

    private static async Task InvokeReadExactlyAsync(Stream stream, byte[] buffer)
    {
        var task = Assert.IsAssignableFrom<Task>(ReadExactlyAsyncMethod.Invoke(null, [stream, buffer, CancellationToken.None]));
        await task;
    }

    private static byte[] InvokeCreateDataChannelFrame(byte frameType, ReadOnlyMemory<byte> payload) =>
        Assert.IsType<byte[]>(CreateDataChannelFrameMethod.Invoke(null, [frameType, payload]));
}

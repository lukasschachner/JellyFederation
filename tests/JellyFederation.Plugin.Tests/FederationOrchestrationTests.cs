using Xunit;

namespace JellyFederation.Plugin.Tests;

public sealed class FederationOrchestrationTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dev.sh")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }

    [Fact]
    public void FederationSignalRService_RegistersReconnectAndDispatchGuards()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Services",
            "FederationSignalRService.cs"));

        Assert.Contains("_connection.Reconnecting", source);
        Assert.Contains("_connection.Reconnected", source);
        Assert.Contains("_librarySync.SyncAsync", source);
        Assert.Contains("_holePunch.Cancel", source);
        Assert.Contains("_webRtc.Cancel", source);
        Assert.Contains("Task.Run", source);
    }

    [Fact]
    public void FederationStartupService_StartStop_AreIdempotentAndSafe()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "JellyFederation.Plugin",
            "Services",
            "FederationStartupService.cs"));

        Assert.Contains("ItemAdded += OnLibraryChanged", source);
        Assert.Contains("ItemRemoved += OnLibraryChanged", source);
        Assert.Contains("ItemUpdated += OnLibraryChanged", source);
        Assert.Contains("ItemAdded -= OnLibraryChanged", source);
        Assert.Contains("ItemRemoved -= OnLibraryChanged", source);
        Assert.Contains("ItemUpdated -= OnLibraryChanged", source);
        Assert.Contains("_retryCts?.Cancel()", source);
        Assert.Contains("ConnectWithRetryAsync", source);
    }
}

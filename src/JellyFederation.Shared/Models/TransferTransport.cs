namespace JellyFederation.Shared.Models;

public enum TransferTransportMode
{
    ArqUdp = 0,
    Quic = 1
}

public enum TransferSelectionReason
{
    DefaultArq = 0,
    LargeFileQuic = 1,
    QuicUnsupportedPeer = 2,
    QuicUnavailableLocal = 3,
    NegotiationFailed = 4,
    FallbackAfterError = 5
}

public enum TransferFailureCategory
{
    Timeout = 0,
    Connectivity = 1,
    Reliability = 2,
    Cancelled = 3,
    Unknown = 4
}
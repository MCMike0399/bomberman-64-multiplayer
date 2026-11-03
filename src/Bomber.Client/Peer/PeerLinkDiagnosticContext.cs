namespace Bomber.Client.Peer;

/// <summary>Optional QA identity; never serialized onto the transport.</summary>
public sealed record PeerLinkDiagnosticContext(string LocalPlayerId, string RemotePlayerId, int? Epoch,
    bool Offerer, int LocalGeneration, int RemoteGeneration, int? AcceptedSignalGeneration = null);

using System.Buffers;
using System.IO.Compression;

namespace Bomber.Client.Peer;

/// <summary>Bounded native data-channel packets; oversized frames use the authenticated relay.</summary>
public static class PeerPacketCodec
{
    public const int MaximumWireBytes = 16384;
    public const int MaximumDecodedBytes = 131072;
    public static bool TryEncode(ReadOnlySpan<byte> json, out byte[] packet)
    {
        packet = [];
        if (json.Length is 0 or > MaximumDecodedBytes || json[0] != (byte)'{') return false;
        if (json.Length <= 1200) { packet = json.ToArray(); return true; }
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumWireBytes);
        try
        {
            if (BrotliEncoder.TryCompress(json, buffer.AsSpan(3, MaximumWireBytes - 3), out int written, quality: 1, window: 22) && written + 3 < json.Length)
            { buffer[0] = 0x42; buffer[1] = 0x43; buffer[2] = 1; packet = buffer.AsSpan(0, written + 3).ToArray(); return true; }
            if (json.Length > MaximumWireBytes) return false;
            packet = json.ToArray(); return true;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    public static bool TryDecode(ReadOnlySpan<byte> packet, out byte[] json)
    {
        json = [];
        if (packet.Length is 0 or > MaximumWireBytes) return false;
        if (packet[0] == (byte)'{') { json = packet.ToArray(); return true; }
        if (packet.Length < 4 || packet[0] != 0x42 || packet[1] != 0x43 || packet[2] != 1) return false;
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDecodedBytes);
        var decoder = new BrotliDecoder();
        try
        {
            var status = decoder.Decompress(packet[3..], buffer.AsSpan(0, MaximumDecodedBytes), out int consumed, out int written);
            if (status != OperationStatus.Done || consumed != packet.Length - 3 || written == 0 || buffer[0] != (byte)'{') return false;
            json = buffer.AsSpan(0, written).ToArray(); return true;
        }
        finally { decoder.Dispose(); ArrayPool<byte>.Shared.Return(buffer); }
    }
}

using System.Text;
using Bomber.Client.Peer;
using Bomber.Game.Matches;

internal static class PeerPacketChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var match = new LocalMatch(new MatchOptions { Stage = "top-rules", HumanPlayers = 2, BotPlayers = 6 });
        var stage = Encoding.UTF8.GetBytes(match.StageJson!);
        Check(stage.Length > PeerPacketCodec.MaximumWireBytes, "real layered stage exercises the native packet-size boundary");
        Check(PeerPacketCodec.TryEncode(stage, out var packet) && packet.Length <= PeerPacketCodec.MaximumWireBytes,
            "real layered stage fits the native channel through bounded encoding");
        Check(PeerPacketCodec.TryDecode(packet, out var decoded) && stage.SequenceEqual(decoded), "native encoding preserves every authored stage byte");
        Check(!PeerPacketCodec.TryEncode(new byte[PeerPacketCodec.MaximumDecodedBytes + 1], out _), "oversized source frames are refused");
        Check(!PeerPacketCodec.TryDecode(new byte[PeerPacketCodec.MaximumWireBytes + 1], out _), "oversized native packets are refused before decoding");
        Check(!PeerPacketCodec.TryDecode([0x42, 0x43, 1, 0xff], out _), "malformed compressed data never reaches JSON rendering");
        Console.WriteLine($"PASS {count} peer packet checks"); return count;
    }
}

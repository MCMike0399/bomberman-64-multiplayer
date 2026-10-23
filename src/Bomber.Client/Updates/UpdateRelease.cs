using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bomber.Client.Updates;

public sealed record UpdateRelease(string Version, string Sha256, long Size)
{
    public const string Repository = "MCMike0399/bomberman-64-multiplayer";
    public const string ArchiveName = "Blast-Circuit-macos-arm64.zip";
    public const string ManifestName = "Blast-Circuit-update.json";
    public static readonly Uri Feed = new($"https://api.github.com/repos/{Repository}/releases/latest");
    public Uri ArchiveUrl => new($"https://github.com/{Repository}/releases/download/v{Version}/{ArchiveName}");
    public static Version ParseVersion(string value)
    {
        if (value.Length > 32 || !Regex.IsMatch(value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)
            || !System.Version.TryParse(value, out var version)) throw new InvalidDataException("Invalid update version.");
        return version;
    }
    public static string PublicKey
    {
        get { using var stream = typeof(UpdateRelease).Assembly.GetManifestResourceStream("BlastCircuit.UpdatePublicKey")!; using var reader = new StreamReader(stream); return reader.ReadToEnd().Trim(); }
    }
    public static UpdateRelease VerifyManifest(byte[] manifest, string signature, string publicKey)
    {
        if (manifest.Length > 65536 || signature.Length > 1024) throw new InvalidDataException("Update manifest is too large.");
        using var key = ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        if (!key.VerifyData(manifest, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new InvalidDataException("Update signature verification failed.");
        using var doc = JsonDocument.Parse(manifest);var data = doc.RootElement;
        if (data.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported update manifest.");
        var release = new UpdateRelease(data.GetProperty("version").GetString()!, data.GetProperty("sha256").GetString()!, data.GetProperty("size").GetInt64());
        ParseVersion(release.Version);
        if (!Regex.IsMatch(release.Sha256, "^[a-f0-9]{64}$") || release.Size is < 1 or > 314572800)
            throw new InvalidDataException("Invalid update archive metadata.");
        return release;
    }
    public static Uri AssetUrl(JsonElement release, string name, string version)
    {
        var asset = release.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name);
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        var expected = $"https://github.com/{Repository}/releases/download/v{version}/{name}";
        if (uri.AbsoluteUri != expected) throw new InvalidDataException("Update asset is outside the official release.");
        return uri;
    }
}

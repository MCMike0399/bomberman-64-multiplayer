using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Bomber.Client.Updates;

/// <summary>Bounded HTTPS downloads, authenticated release metadata, and verified archive staging.</summary>
public sealed class UpdateService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _key;
    public UpdateService(HttpMessageHandler? handler = null, string? publicKey = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BlastCircuit-Updater/1.0");
        _key = publicKey ?? UpdateRelease.PublicKey;
    }
    public async Task<UpdateRelease?> Check(string currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Get(UpdateRelease.Feed, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBounded(response, 2 * 1024 * 1024, timeout.Token);
        using var json = JsonDocument.Parse(bytes);var data = json.RootElement;
        if (data.GetProperty("draft").GetBoolean() || data.GetProperty("prerelease").GetBoolean()) return null;
        var tag = data.GetProperty("tag_name").GetString()!;
        if (!tag.StartsWith('v')) throw new InvalidDataException("Invalid release tag.");
        var version = tag[1..];
        if (UpdateRelease.ParseVersion(version) <= UpdateRelease.ParseVersion(currentVersion)) return null;
        using var manifestResponse = await Get(UpdateRelease.AssetUrl(data, UpdateRelease.ManifestName, version), timeout.Token);
        manifestResponse.EnsureSuccessStatusCode();var manifest = await ReadBounded(manifestResponse, 65536, timeout.Token);
        using var signatureResponse = await Get(UpdateRelease.AssetUrl(data, UpdateRelease.ManifestName + ".sig", version), timeout.Token);
        signatureResponse.EnsureSuccessStatusCode();var signature = System.Text.Encoding.ASCII.GetString(await ReadBounded(signatureResponse, 1024, timeout.Token));
        var release = UpdateRelease.VerifyManifest(manifest, signature, _key);
        if (release.Version != version) throw new InvalidDataException("Signed version does not match the release tag.");
        UpdateRelease.AssetUrl(data, UpdateRelease.ArchiveName, version);
        return release;
    }
    public async Task Download(UpdateRelease release, string destination, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await Get(release.ArchiveUrl, timeout.Token);response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is {} length && length != release.Size) throw new InvalidDataException("Update size differs from its signed manifest.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];long total = 0;int read;
        while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
        {
            total += read;if (total > release.Size) throw new InvalidDataException("Update exceeds its signed size.");
            hash.AppendData(buffer, 0, read);await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }
        if (total != release.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != release.Sha256)
            throw new InvalidDataException("Update archive is incomplete or has been modified.");
    }
    private async Task<HttpResponseMessage> Get(Uri uri, CancellationToken ct)
    {
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Port != 443 || uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Update download must use the official HTTPS hosts.");
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;response.Dispose();
                if (location is null) throw new InvalidDataException("Missing update redirect.");
                uri = new Uri(uri, location);continue;
            }
            return response;
        }
        throw new InvalidDataException("Too many update redirects.");
    }
    private static async Task<byte[]> ReadBounded(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        await using var input = await response.Content.ReadAsStreamAsync(ct);using var output = new MemoryStream();
        var buffer = new byte[8192];int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        { if (output.Length + read > limit) throw new InvalidDataException("Update response is too large.");output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    public void Dispose() => _http.Dispose();
}

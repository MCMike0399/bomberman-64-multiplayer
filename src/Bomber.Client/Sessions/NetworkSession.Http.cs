using System.Net.Http.Json;
using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Online;

namespace Bomber.Client.Sessions;

/// <summary>Bounded, release-checked HTTP requests to the kernel realtime API.</summary>
public sealed partial class NetworkSession
{
    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        ct = timeout.Token;
        using var request = new HttpRequestMessage(method, new Uri(_origin, "/realtime/v1/" + path));
        request.Headers.Add(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
        if (_token is not null) request.Headers.Add("x-player-token", _token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        // Bound bodies before parsing, even if the server omits Content-Length.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bounded = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes.AsMemory(), ct)) != 0)
        {
            if (bounded.Length + count > 1024 * 1024) throw new HttpRequestException("Server response is too large.");
            bounded.Write(bytes, 0, count);
        }
        using var json = JsonDocument.Parse(bounded.ToArray());
        if (!response.IsSuccessStatusCode)
        {
            // A server from another release cannot tell whether this client is older or newer.
            // Explain our required server release instead of instructing an updated player to downgrade.
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict &&
                json.RootElement.TryGetProperty("error", out var compatibilityError) &&
                compatibilityError.TryGetProperty("code", out var code) && code.GetString() == "client_update_required")
            {
                if (compatibilityError.TryGetProperty("requiredVersion", out var required) && required.ValueKind == JsonValueKind.String &&
                    Version.TryParse(required.GetString(), out var serverVersion) && serverVersion > Version.Parse(OnlineProtocol.ReleaseVersion))
                    throw new InvalidOperationException(compatibilityError.GetProperty("message").GetString() ?? OnlineProtocol.ServerUpdateMessage);
                throw new InvalidOperationException(OnlineProtocol.ServerUpdateMessage);
            }
            var detail = json.RootElement.TryGetProperty("error", out var error) ? error.GetProperty("message").GetString() : response.ReasonPhrase;
            throw new HttpRequestException(detail ?? "Server refused the request.", null, response.StatusCode);
        }
        if(path=="guests" && (!response.Headers.TryGetValues(OnlineProtocol.HeaderName,out var protocol)||!OnlineProtocol.Accepts(protocol)))
            throw new InvalidOperationException(OnlineProtocol.ServerUpdateMessage);
        return json.RootElement.Clone();
    }
}

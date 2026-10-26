using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Online;

internal static class ServerCompatibilityChecks
{
    public static async Task<int> Run()
    {
        int checks=0;
        if (OnlineProtocol.Accepts(["3"])) throw new Exception("A new release must invalidate the previous release's online admission token");
        checks++;
        foreach(string[]? advertised in new string[]?[]{null,["1"],["2"],["3"],["4"],["5"],["6"],[$"{OnlineProtocol.Revision}/0.0.0/1"],[$"{OnlineProtocol.Revision}/999.0.0/999"],[OnlineProtocol.WireRevision,OnlineProtocol.WireRevision],[OnlineProtocol.WireRevision]})
        {
            using var handler=new Reply(advertised);
            var constructor=typeof(NetworkSession).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(Uri),typeof(HttpMessageHandler)],null)!;
            await using var client=(NetworkSession)constructor.Invoke([new Uri("http://compatibility.invalid"),handler]);
            var method=typeof(NetworkSession).GetMethod("RequestAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var request=(Task<JsonElement>)method.Invoke(client,[HttpMethod.Post,"guests",new{displayName="Protocol fixture"},CancellationToken.None])!;
            bool accepted=true;
            try{await request;}catch(InvalidOperationException e)when(e.Message==OnlineProtocol.ServerUpdateMessage){accepted=false;}
            if(accepted!=OnlineProtocol.Accepts(advertised)||!handler.SawClientHeader)throw new Exception("HTTP server capability validation failed");
            checks++;
        }
        using (var handler = new Reply(["3"], true))
        {
            var constructor=typeof(NetworkSession).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(Uri),typeof(HttpMessageHandler)],null)!;
            await using var client=(NetworkSession)constructor.Invoke([new Uri("http://compatibility.invalid"),handler]);
            var method=typeof(NetworkSession).GetMethod("RequestAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var request=(Task<JsonElement>)method.Invoke(client,[HttpMethod.Post,"guests",new{displayName="Updated client"},CancellationToken.None])!;
            try { await request; throw new Exception("A stale server must be explained to an updated client"); }
            catch (InvalidOperationException error) when (error.Message == OnlineProtocol.ServerUpdateMessage) { checks++; }
        }
        using (var handler = new Reply(["4/999.0.0/999"], true, "999.0.0"))
        {
            var constructor=typeof(NetworkSession).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(Uri),typeof(HttpMessageHandler)],null)!;
            await using var client=(NetworkSession)constructor.Invoke([new Uri("http://compatibility.invalid"),handler]);
            var method=typeof(NetworkSession).GetMethod("RequestAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var request=(Task<JsonElement>)method.Invoke(client,[HttpMethod.Post,"guests",new{displayName="Old client"},CancellationToken.None])!;
            try { await request; throw new Exception("A newer server must instruct the older client to update"); }
            catch (InvalidOperationException error) when (error.Message == "Update to future fixture") { checks++; }
        }
        Console.WriteLine($"PASS {checks} server-version response checks");return checks;
    }
    private sealed class Reply(string[]? protocol, bool reject = false, string? requiredVersion = null):HttpMessageHandler
    {
        public bool SawClientHeader;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            SawClientHeader=request.Headers.TryGetValues(OnlineProtocol.HeaderName,out var values)&&OnlineProtocol.Accepts(values);
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"playerId\":\"fixture\",\"token\":\"fixture-only\"}",Encoding.UTF8,"application/json")};
            if (reject)
            {
                response.StatusCode=HttpStatusCode.Conflict;
                response.Content=new StringContent("{\"error\":{\"code\":\"client_update_required\",\"message\":\"Update to the old release\"}}",Encoding.UTF8,"application/json");
                if (requiredVersion is not null)
                    response.Content=new StringContent(JsonSerializer.Serialize(new { error = new { code = "client_update_required", message = "Update to future fixture", requiredVersion } }),Encoding.UTF8,"application/json");
            }
            if(protocol is not null)response.Headers.TryAddWithoutValidation(OnlineProtocol.HeaderName,protocol);
            return Task.FromResult(response);
        }
    }
}

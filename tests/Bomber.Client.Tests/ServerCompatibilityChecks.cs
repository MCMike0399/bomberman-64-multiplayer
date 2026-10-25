using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;

internal static class ServerCompatibilityChecks
{
    public static async Task<int> Run()
    {
        int checks=0;
        foreach(string[]? advertised in new string[]?[]{null,["1"],["2"],[OnlineProtocol.WireRevision,OnlineProtocol.WireRevision],[OnlineProtocol.WireRevision]})
        {
            using var handler=new Reply(advertised);
            var constructor=typeof(NetworkSession).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(Uri),typeof(HttpMessageHandler)],null)!;
            await using var client=(NetworkSession)constructor.Invoke([new Uri("http://compatibility.invalid"),handler]);
            var method=typeof(NetworkSession).GetMethod("RequestAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var request=(Task<JsonElement>)method.Invoke(client,[HttpMethod.Post,"guests",new{displayName="Protocol fixture"},CancellationToken.None])!;
            bool accepted=true;
            try{await request;}catch(InvalidOperationException e)when(e.Message.Contains("incompatible stage rules")){accepted=false;}
            if(accepted!=OnlineProtocol.Accepts(advertised)||!handler.SawClientHeader)throw new Exception("HTTP server capability validation failed");
            checks++;
        }
        Console.WriteLine($"PASS {checks} server-version response checks");return checks;
    }
    private sealed class Reply(string[]? protocol):HttpMessageHandler
    {
        public bool SawClientHeader;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            SawClientHeader=request.Headers.TryGetValues(OnlineProtocol.HeaderName,out var values)&&OnlineProtocol.Accepts(values);
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"playerId\":\"fixture\",\"token\":\"fixture-only\"}",Encoding.UTF8,"application/json")};
            if(protocol is not null)response.Headers.TryAddWithoutValidation(OnlineProtocol.HeaderName,protocol);
            return Task.FromResult(response);
        }
    }
}

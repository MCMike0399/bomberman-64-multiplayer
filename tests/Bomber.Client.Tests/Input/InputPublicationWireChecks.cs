using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.Testing;

/// <summary>Opt-in, no-GPU regression through an owned loopback product server.
/// No authority mutation: ordinary bomb/carry and movement inputs only.</summary>
internal static class InputPublicationWireChecks
{
    internal static async Task<int> Run(string[] args)
    {
        var portProbe=new TcpListener(IPAddress.Loopback,0);portProbe.Start();int port=((IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
        string serverDll=Path.GetFullPath(args.FirstOrDefault(a=>a.StartsWith("--input-publication-server="))?[27..]??"src/Bomber.Server/bin/Debug/net10.0/Bomber.Server.dll");
        if(!File.Exists(serverDll))throw new FileNotFoundException("Build the product server before the opt-in wire regression",serverDll);
        string evidence=Path.GetFullPath(args.FirstOrDefault(a=>a.StartsWith("--input-publication-output="))?[27..]??"artifacts/nudge-publication-wire");Directory.CreateDirectory(evidence);
        var logPath=Path.Combine(evidence,"owned-server.log");
        var info=new ProcessStartInfo("dotnet"){WorkingDirectory=Directory.GetCurrentDirectory(),RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(serverDll);info.ArgumentList.Add("--urls");info.ArgumentList.Add($"http://127.0.0.1:{port}");
        using var server=Process.Start(info)??throw new Exception("Cannot launch owned fixture server");
        using var log=new StreamWriter(logPath){AutoFlush=true};server.OutputDataReceived+=(_,e)=>{if(e.Data!=null)lock(log)log.WriteLine(e.Data);};server.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)lock(log)log.WriteLine(e.Data);};server.BeginOutputReadLine();server.BeginErrorReadLine();
        try
        {
            var origin=new Uri($"http://127.0.0.1:{port}");using var http=new HttpClient();var start=Stopwatch.StartNew();
            while(true){try{if((await http.GetAsync(new Uri(origin,"/game"))).IsSuccessStatusCode)break;}catch(HttpRequestException){}if(start.Elapsed.TotalSeconds>15||server.HasExited)throw new Exception("Owned server did not start");await Task.Delay(50);}
            var settings=new SessionSettings(Bots:0,SuddenDeath:false){Hosting="server",Advanced=new AdvancedRules{BlockPercent=0}};
            await using var host=await NetworkSession.ConnectAsync(origin,"Nudge wire host",settings,NetworkMode.CreatePrivate);
            await using var guest=await NetworkSession.ConnectAsync(origin,"Nudge wire guest",settings,NetworkMode.JoinCode,host.JoinCode);
            JsonElement? latest=null;
            void Pump(){host.Advance(1d/30);guest.Advance(1d/30);foreach(var message in host.DrainMessages())if(message.GetProperty("t").GetString()=="state")latest=message.GetProperty("d").Clone();guest.DrainMessages();}
            async Task Until(Func<bool> ready,string label,int milliseconds=10000){var timer=Stopwatch.StartNew();while(!ready()){Pump();if(timer.ElapsedMilliseconds>milliseconds)throw new TimeoutException(label);await Task.Delay(5);}}
            JsonElement Actor()=>latest!.Value.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==host.PlayerId);
            await Until(()=>host.Party?.CanStart==true,"private cohort readiness");host.RequestStart();await Until(()=>latest is {} s&&s.GetProperty("phase").GetInt32()==1,"real authority fight");
            var hold=new PlayerInput(0,0,B:true);var press=new PlayerInput(-1,0,B:true);
            host.SendInput(0,new PlayerInput(0,0,A:true,B:true));await Task.Delay(120);host.SendInput(0,hold);
            await Until(()=>Actor().GetProperty("carrying").GetString()=="bomb","ordinary carried bomb");
            float initialX=Actor().GetProperty("x").GetSingle();long started=Stopwatch.GetTimestamp(),baseline=host.LatestInputSend?.TotalSentSamples??0;
            var gate=new ObservedInputNudge(press,host.ConnectionGeneration,baseline,started,Stopwatch.Frequency*5,(long)(Stopwatch.Frequency*(1.25-.25)/4.5));
            // The game frame samples before the observer releases, then is deliberately paused before publishing.
            var publisher=new FixtureInputPublication();var before=publisher.Capture(host,0);
            var sampledFrameInput=gate.Pressed(null,started,host.ConnectionGeneration)?press:hold;
            using var publication=publisher.Own(host,0);var during=publisher.Capture(host,0);
            if(!publication.Publish(press))throw new Exception("Owner could not publish its ordinary nudge");
            await Until(()=>host.LatestInputSend is {} p&&p.ContainsNewSends(press,host.ConnectionGeneration,baseline,2),"two real pressed sends");
            gate.Pressed(host.LatestInputSend,Stopwatch.GetTimestamp(),host.ConnectionGeneration);
            if(gate.Decision is not {Delivered:true})throw new Exception("Nudge did not release from real completed sends");
            if(!publication.Publish(hold))throw new Exception("Owner could not publish neutral");
            await Until(()=>ObservedInputNudge.NeutralSent(host.LatestInputSend,hold,host.ConnectionGeneration,gate.Decision!),"actual neutral send");
            long neutral=host.LatestInputSend!.SentSequence;await Until(()=>Actor().GetProperty("ack").GetInt64()>=neutral,"actual authority neutral acknowledgment");
            Console.WriteLine($"NEUTRAL_SENT_ACK seq={neutral} ack={Actor().GetProperty("ack").GetInt64()} x={Actor().GetProperty("x").GetSingle():F3}");
            publication.Dispose();
            bool rejectedBefore=!publisher.PublishFrame(before,sampledFrameInput),rejectedDuring=!publisher.PublishFrame(during,sampledFrameInput);
            var waited=Stopwatch.StartNew();while(waited.ElapsedMilliseconds<300){Pump();await Task.Delay(5);}
            if(!publisher.PublishFrame(publisher.Capture(host,0),hold))throw new Exception("Fresh post-lease frame was not restored");
            await Until(()=>host.LatestInputSend is {} p&&p.Input==hold&&p.SentSequence>neutral,"fresh neutral send");long final=host.LatestInputSend!.SentSequence;
            await Until(()=>Actor().GetProperty("ack").GetInt64()>=final,"fresh neutral authority acknowledgment");
            float distance=Math.Abs(initialX-Actor().GetProperty("x").GetSingle());
            Console.WriteLine($"STALE_BEFORE_ACQUISITION_REJECTED={rejectedBefore} STALE_WHILE_OWNED_AFTER_DISPOSE_REJECTED={rejectedDuring}");
            Console.WriteLine($"NEUTRAL_REMAINS_SENT seq={final} ack={Actor().GetProperty("ack").GetInt64()} distance={distance:F3} corridor=1.250 alive={Actor().GetProperty("alive").GetBoolean()} y={Actor().GetProperty("y").GetSingle():F2}");
            if(!rejectedBefore||!rejectedDuring||distance>1.25f)throw new Exception("Stale frame publication or verified corridor regression");
            File.WriteAllText(Path.Combine(evidence,"input-publication-wire.json"),JsonSerializer.Serialize(new{
                schemaVersion=1,scope="Owned loopback product server; ordinary inputs, no authority mutation or renderer",passed=true,
                rejectedBefore,rejectedDuring,neutralSent=neutral,finalSent=final,finalAck=Actor().GetProperty("ack").GetInt64(),
                initialX,finalX=Actor().GetProperty("x").GetSingle(),distance,corridor=1.25f,
                alive=Actor().GetProperty("alive").GetBoolean(),height=Actor().GetProperty("y").GetSingle()},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS: both stale frame interleavings stay neutral through the real send/ack path");return 1;
        }
        finally{if(!server.HasExited){server.Kill(entireProcessTree:true);await server.WaitForExitAsync();}}
    }
}

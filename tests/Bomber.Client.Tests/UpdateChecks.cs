using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Bomber.Client.Updates;

internal static class UpdateChecks
{
    public static async Task<int> Run()
    {
        int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception("Update: "+text);checks++;}
        void Reject(Action action,string text){try{action();}catch(Exception e)when(e is InvalidDataException or CryptographicException or FormatException or InvalidOperationException){checks++;return;}throw new Exception("Update should reject "+text);}
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        byte[] data="verified game payload"u8.ToArray();
        var metadata=new{schemaVersion=1,version="0.22.0",sha256=Convert.ToHexStringLower(SHA256.HashData(data)),size=data.Length};
        byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(metadata);
        string signature=Convert.ToBase64String(key.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence));
        var release=UpdateRelease.VerifyManifest(manifest,signature,publicKey);
        Check(release.Version=="0.22.0","valid signature and version");
        var tampered=manifest.ToArray();tampered[10]^=1;Reject(()=>UpdateRelease.VerifyManifest(tampered,signature,publicKey),"tampered manifest");
        using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Reject(()=>UpdateRelease.VerifyManifest(manifest,signature,Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())),"wrong signing key");
        foreach(string version in new[]{"1.2","v1.2.3","1.2.3-beta","01.2.3","1.2.3/../x"})Reject(()=>UpdateRelease.ParseVersion(version),"ambiguous version");
        Check(UpdateRelease.ParseVersion("0.21.10")>UpdateRelease.ParseVersion("0.21.9"),"versions compare numerically");
        object Asset(string name)=>new{name,browser_download_url=$"https://github.com/{UpdateRelease.Repository}/releases/download/v0.22.0/{name}"};
        byte[] feed=JsonSerializer.SerializeToUtf8Bytes(new{draft=false,prerelease=false,tag_name="v0.22.0",assets=new[]{Asset(UpdateRelease.ArchiveName),Asset(UpdateRelease.ManifestName),Asset(UpdateRelease.ManifestName+".sig")}});
        var files=new Dictionary<string,byte[]>{{UpdateRelease.Feed.AbsoluteUri,feed},{release.ArchiveUrl.AbsoluteUri,data},{release.ArchiveUrl.AbsoluteUri.Replace(UpdateRelease.ArchiveName,UpdateRelease.ManifestName),manifest},{release.ArchiveUrl.AbsoluteUri.Replace(UpdateRelease.ArchiveName,UpdateRelease.ManifestName+".sig"),System.Text.Encoding.ASCII.GetBytes(signature)}};
        using var service=new UpdateService(new Responses(files),publicKey);
        Check(await service.Check("0.21.0",default)==release,"official feed verifies release metadata");
        Check(await service.Check("0.22.0",default)==null&&await service.Check("0.23.0",default)==null,"equal versions and downgrades are ignored");
        string temp=Path.Combine(Path.GetTempPath(),"blast-updater-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string archive=Path.Combine(temp,"download.zip");await service.Download(release,archive,default);
            Check(File.ReadAllBytes(archive).SequenceEqual(data),"download size/hash verify");
            files[release.ArchiveUrl.AbsoluteUri]="tampered game payload"u8.ToArray();
            try{await service.Download(release,Path.Combine(temp,"bad.zip"),default);throw new Exception("Tampered archive accepted.");}catch(InvalidDataException){checks++;}
            string zip=Path.Combine(temp,"unsafe.zip");
            using(var zipped=ZipFile.Open(zip,ZipArchiveMode.Create))
            {using var writer=new StreamWriter(zipped.CreateEntry("Blast Circuit.app/../outside").Open());writer.Write("bad");}
            Reject(()=>MacUpdateInstaller.ValidateArchive(zip),"archive traversal");
            using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{assets=new[]{new{name=UpdateRelease.ManifestName,browser_download_url="http://example.com/update.json"}}}));
            Reject(()=>UpdateRelease.AssetUrl(json.RootElement,UpdateRelease.ManifestName,"0.22.0"),"foreign/insecure asset URL");
        }
        finally{Directory.Delete(temp,true);}
        using var absent=new UpdateService(new Responses([]),publicKey);
        Check(await absent.Check("0.21.0",default)==null,"empty release feed is normal");
        Console.WriteLine($"PASS {checks} signed update, download and archive boundary checks");return checks;
    }
    private sealed class Responses(Dictionary<string,byte[]> files):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(
            files.TryGetValue(request.RequestUri!.AbsoluteUri,out var content)?new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(content)}:new HttpResponseMessage(HttpStatusCode.NotFound){Content=new ByteArrayContent([])});
    }
}

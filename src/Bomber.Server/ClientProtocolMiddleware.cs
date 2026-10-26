using Bomber.Game.Online;

namespace Bomber.Server;

/// <summary>Reject incompatible players before identity, reservation, queue or socket admission.
/// Operator access and leaving/cancelling remain governed by their existing authentication.</summary>
public sealed class ClientProtocolMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers[OnlineProtocol.HeaderName]=OnlineProtocol.WireRevision;
        string path=(context.Request.Path.Value??"").TrimEnd('/').ToLowerInvariant();
        bool post=HttpMethods.IsPost(context.Request.Method),get=HttpMethods.IsGet(context.Request.Method);
        bool admission=post && (path is "/realtime/v1/guests" or "/realtime/v1/queue" or "/realtime/v1/lobbies" or "/realtime/v1/lobbies/join" ||
            path.StartsWith("/realtime/v1/lobbies/",StringComparison.Ordinal)&&path.EndsWith("/join",StringComparison.Ordinal)) ||
            get && (path=="/realtime/v1/connect" || path.StartsWith("/realtime/v1/queue/",StringComparison.Ordinal));
        if(admission && !OnlineProtocol.Accepts(context.Request.Headers[OnlineProtocol.HeaderName].Select(value=>value??"")))
        {
            context.Response.StatusCode=StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new{error=new{code="client_update_required",message=OnlineProtocol.UpdateMessage,
                requiredVersion=OnlineProtocol.ReleaseVersion, requiredBuild=OnlineProtocol.ReleaseBuild, requiredProtocol=OnlineProtocol.WireRevision}});
            return;
        }
        await next(context);
    }
}

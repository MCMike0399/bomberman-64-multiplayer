namespace Bomber.Client.Updates;

/// <summary>Only explicit launch/health failures quarantine a desktop release.</summary>
public static class DesktopUpdateHistory
{
    public static string? QuarantinedVersion(string result, bool legacyMac = false)
    {
        string[] parts=result.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length<2||parts[0]!="failed"||!Version.TryParse(parts[1],out _))return null;
        if(legacyMac)return parts[1];
        // Older desktop helpers also used launch-rollback for file-move errors.
        // Retry an ambiguous legacy result rather than permanently locking out online play.
        return parts.Length==4&&parts[2]=="launch-rollback"&&parts[3] is "launch" or "health"?parts[1]:null;
    }
}

namespace Bomber.Client.Updates;

/// <summary>Private, same-user handoff to a detached updater outside the installed payload.</summary>
public sealed record DesktopUpdateRequest(string Target, string Stage, string Version, string Token,
    string History, int ParentPid, long ParentStartTicks, bool Windowed, bool AppImage);

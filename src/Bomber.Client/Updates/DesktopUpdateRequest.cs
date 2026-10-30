namespace Bomber.Client.Updates;

/// <summary>Private, same-user handoff to a detached updater outside the installed payload.</summary>
// ParentStartTicks remains for request-format compatibility; ParentIdentity is
// the current helper's authoritative birth identifier across runtime versions.
public sealed record DesktopUpdateRequest(string Target, string Stage, string Version, string Token,
    string History, int ParentPid, long ParentStartTicks, bool Windowed, bool AppImage, string? ParentIdentity = null);

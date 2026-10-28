namespace Jellyfin.Plugin.JellyFix.Matroska;

/// <summary>
/// Outcome of inspecting (and optionally repairing) one Matroska file.
/// </summary>
internal enum SeekIndexStatus
{
    /// <summary>The seek index is reachable and the file needs no change.</summary>
    Ok,

    /// <summary>The file was broken and has been repaired.</summary>
    Fixed,

    /// <summary>The file is broken and repairable, but nothing was written (dry run).</summary>
    Fixable,

    /// <summary>The file is broken in a way this plugin cannot repair in place.</summary>
    Unsupported,

    /// <summary>The file is not a Matroska container.</summary>
    NotMatroska,

    /// <summary>The file could not be read or written.</summary>
    Error
}

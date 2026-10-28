namespace Jellyfin.Plugin.JellyFix.Matroska;

/// <summary>
/// A single EBML element header (id, position and declared size).
/// </summary>
/// <param name="Id">The raw element id, including its length marker.</param>
/// <param name="Start">Absolute offset of the element id in the file.</param>
/// <param name="HeaderLength">Combined length of the id and size fields.</param>
/// <param name="Size">Declared payload size in bytes, or -1 when the size is unknown.</param>
internal readonly record struct EbmlElement(uint Id, long Start, int HeaderLength, long Size)
{
    /// <summary>
    /// Gets the absolute offset of the element payload.
    /// </summary>
    public long DataOffset => Start + HeaderLength;

    /// <summary>
    /// Gets a value indicating whether the element uses the "unknown size" encoding.
    /// </summary>
    public bool IsUnknownSize => Size < 0;
}

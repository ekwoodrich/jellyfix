namespace Jellyfin.Plugin.JellyFix.Matroska;

/// <summary>
/// One entry of a Matroska SeekHead, including the exact file offsets of its id and
/// position payloads so that the entry can be retargeted in place.
/// </summary>
/// <param name="Id">The referenced element id bytes.</param>
/// <param name="Position">The referenced element position, relative to the segment payload.</param>
/// <param name="IdPayloadOffset">Absolute offset of the SeekID payload.</param>
/// <param name="IdPayloadLength">Length of the SeekID payload in bytes.</param>
/// <param name="PositionPayloadOffset">Absolute offset of the SeekPosition payload.</param>
/// <param name="PositionPayloadLength">Length of the SeekPosition payload in bytes.</param>
internal sealed record SeekEntry(
    byte[] Id,
    long Position,
    long IdPayloadOffset,
    int IdPayloadLength,
    long PositionPayloadOffset,
    int PositionPayloadLength)
{
    /// <summary>
    /// Gets a value indicating whether this entry references the Cues element (0x1C53BB6B).
    /// </summary>
    public bool ReferencesCues => Id is [0x1C, 0x53, 0xBB, 0x6B];

    /// <summary>
    /// Gets a value indicating whether this entry references another SeekHead (0x114D9B74).
    /// </summary>
    public bool ReferencesSeekHead => Id is [0x11, 0x4D, 0x9B, 0x74];
}

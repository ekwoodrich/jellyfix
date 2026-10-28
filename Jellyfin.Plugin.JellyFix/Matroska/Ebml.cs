using System;
using System.IO;

namespace Jellyfin.Plugin.JellyFix.Matroska;

/// <summary>
/// The handful of EBML primitives required to walk a Matroska header.
/// </summary>
internal static class Ebml
{
    /// <summary>EBML header element id.</summary>
    public const uint EbmlHeaderId = 0x1A45DFA3;

    /// <summary>Segment element id.</summary>
    public const uint SegmentId = 0x18538067;

    /// <summary>SeekHead element id.</summary>
    public const uint SeekHeadId = 0x114D9B74;

    /// <summary>Seek entry element id.</summary>
    public const uint SeekEntryId = 0x4DBB;

    /// <summary>SeekID element id.</summary>
    public const uint SeekIdElementId = 0x53AB;

    /// <summary>SeekPosition element id.</summary>
    public const uint SeekPositionElementId = 0x53AC;

    /// <summary>Cues element id.</summary>
    public const uint CuesId = 0x1C53BB6B;

    /// <summary>Cluster element id.</summary>
    public const uint ClusterId = 0x1F43B675;

    /// <summary>
    /// Reads an EBML element id. The length marker is preserved so that the value matches
    /// the raw bytes of the id (for example <c>0x1C53BB6B</c> for Cues).
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="id">The element id.</param>
    /// <param name="length">Number of bytes consumed by the id.</param>
    /// <returns><see langword="true"/> when an id was read.</returns>
    public static bool TryReadId(Stream stream, out uint id, out int length)
    {
        id = 0;
        length = 0;

        var first = stream.ReadByte();
        if (first < 0)
        {
            return false;
        }

        if (first == 0)
        {
            throw new InvalidDataException("EBML element id starts with a zero byte.");
        }

        var mask = 0x80;
        var byteCount = 1;
        while ((first & mask) == 0)
        {
            mask >>= 1;
            byteCount++;
            if (byteCount > 4)
            {
                throw new InvalidDataException("EBML element id is longer than 4 bytes.");
            }
        }

        var value = (uint)first;
        for (var i = 1; i < byteCount; i++)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                return false;
            }

            value = (value << 8) | (uint)next;
        }

        id = value;
        length = byteCount;
        return true;
    }

    /// <summary>
    /// Reads an EBML size field.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="size">The decoded size, or -1 for the "unknown size" encoding.</param>
    /// <param name="length">Number of bytes consumed by the size field.</param>
    /// <returns><see langword="true"/> when a size was read.</returns>
    public static bool TryReadSize(Stream stream, out long size, out int length)
    {
        size = 0;
        length = 0;

        var first = stream.ReadByte();
        if (first < 0)
        {
            return false;
        }

        if (first == 0)
        {
            throw new InvalidDataException("EBML size field starts with a zero byte.");
        }

        var mask = 0x80;
        var byteCount = 1;
        while ((first & mask) == 0)
        {
            mask >>= 1;
            byteCount++;
            if (byteCount > 8)
            {
                throw new InvalidDataException("EBML size field is longer than 8 bytes.");
            }
        }

        var value = (long)(first & (mask - 1));
        for (var i = 1; i < byteCount; i++)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                return false;
            }

            value = (value << 8) | (uint)next;
        }

        var unknown = value == (1L << (7 * byteCount)) - 1;
        size = unknown ? -1 : value;
        length = byteCount;
        return true;
    }

    /// <summary>
    /// Reads an element header at the current stream position.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="element">The element header.</param>
    /// <returns><see langword="true"/> when a header was read.</returns>
    public static bool TryReadElement(Stream stream, out EbmlElement element)
    {
        element = default;
        var start = stream.Position;
        if (!TryReadId(stream, out var id, out var idLength))
        {
            return false;
        }

        if (!TryReadSize(stream, out var size, out var sizeLength))
        {
            return false;
        }

        element = new EbmlElement(id, start, idLength + sizeLength, size);
        return true;
    }
}

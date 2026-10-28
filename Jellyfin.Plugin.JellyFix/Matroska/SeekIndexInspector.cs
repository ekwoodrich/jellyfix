using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Jellyfin.Plugin.JellyFix.Matroska;

/// <summary>
/// Detects and repairs Matroska files whose seek index cannot be found by ExoPlayer.
/// </summary>
/// <remarks>
/// Some muxers write a SeekHead that contains only a single entry pointing at a second
/// SeekHead, with the Cues (seek index) reference living in that second SeekHead near the
/// end of the file. ExoPlayer's Matroska extractor only reads the first SeekHead, never
/// learns where Cues is, reports the stream as not seekable, and clients restart playback
/// from the beginning whenever the user seeks.
/// The repair retargets that first SeekHead entry to the real Cues element. It is a
/// 12 byte, in-place, lossless edit: no media data is copied and no offsets move.
/// </remarks>
internal static class SeekIndexInspector
{
    private const int MaxSeekHeadChainDepth = 4;
    private const int MaxSeekIdLength = 16;

    /// <summary>
    /// Inspects a file and, when <paramref name="fix"/> is <see langword="true"/>, repairs it.
    /// </summary>
    /// <param name="path">Path of the Matroska file.</param>
    /// <param name="fix">Whether to write the repaired seek index.</param>
    /// <returns>The inspection result.</returns>
    public static InspectionResult Inspect(string path, bool fix)
    {
        try
        {
            var access = fix ? FileAccess.ReadWrite : FileAccess.Read;
            using var stream = new FileStream(path, FileMode.Open, access, FileShare.ReadWrite | FileShare.Delete);
            return Inspect(stream, fix);
        }
        catch (IOException ex)
        {
            return new InspectionResult(SeekIndexStatus.Error, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new InspectionResult(SeekIndexStatus.Error, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return new InspectionResult(SeekIndexStatus.Error, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return new InspectionResult(SeekIndexStatus.Unsupported, ex.Message);
        }
    }

    private static InspectionResult Inspect(FileStream stream, bool fix)
    {
        stream.Position = 0;
        var fileLength = stream.Length;

        if (!Ebml.TryReadElement(stream, out var ebmlHeader) || ebmlHeader.Id != Ebml.EbmlHeaderId)
        {
            return new InspectionResult(SeekIndexStatus.NotMatroska, "Missing EBML header.");
        }

        var segmentStart = ebmlHeader.DataOffset + ebmlHeader.Size;
        if (segmentStart <= 0 || segmentStart >= fileLength)
        {
            return new InspectionResult(SeekIndexStatus.NotMatroska, "Missing Segment element.");
        }

        stream.Position = segmentStart;
        if (!Ebml.TryReadElement(stream, out var segment) || segment.Id != Ebml.SegmentId)
        {
            return new InspectionResult(SeekIndexStatus.NotMatroska, "Missing Segment element.");
        }

        var segmentDataStart = segment.DataOffset;
        var segmentDataEnd = segment.IsUnknownSize
            ? fileLength
            : Math.Min(fileLength, segmentDataStart + segment.Size);

        var firstSeekHead = FindFirstSeekHead(stream, segmentDataStart, segmentDataEnd);
        if (firstSeekHead is null)
        {
            return new InspectionResult(SeekIndexStatus.Unsupported, "No SeekHead before the first cluster.");
        }

        var entries = ReadSeekEntries(stream, firstSeekHead.Value);
        foreach (var entry in entries)
        {
            if (entry.ReferencesCues)
            {
                return new InspectionResult(SeekIndexStatus.Ok, "The first SeekHead references Cues directly.");
            }
        }

        var chain = FindCuesThroughChain(stream, segmentDataStart, fileLength, entries);
        if (chain is null)
        {
            return new InspectionResult(SeekIndexStatus.Unsupported, "No Cues element is reachable from the first SeekHead.");
        }

        var (chainEntry, cuesPosition) = chain.Value;
        var cuesDescription = string.Format(CultureInfo.InvariantCulture, "Cues found at segment offset {0}.", cuesPosition);

        if (!fix)
        {
            return new InspectionResult(SeekIndexStatus.Fixable, "The first SeekHead points at another SeekHead; " + cuesDescription)
            {
                CuesPosition = cuesPosition
            };
        }

        if (chainEntry.IdPayloadLength != 4)
        {
            return new InspectionResult(SeekIndexStatus.Unsupported, "The SeekHead entry id is not 4 bytes wide, so it cannot be retargeted to Cues.")
            {
                CuesPosition = cuesPosition
            };
        }

        if (!FitsIn(cuesPosition, chainEntry.PositionPayloadLength))
        {
            return new InspectionResult(SeekIndexStatus.Unsupported, "The Cues position does not fit in the existing SeekPosition field.")
            {
                CuesPosition = cuesPosition
            };
        }

        stream.Position = chainEntry.IdPayloadOffset;
        stream.Write([0x1C, 0x53, 0xBB, 0x6B]);
        stream.Position = chainEntry.PositionPayloadOffset;
        WriteBigEndian(stream, cuesPosition, chainEntry.PositionPayloadLength);
        stream.Flush();

        var verification = Inspect(stream, false);
        if (verification.Status != SeekIndexStatus.Ok)
        {
            return new InspectionResult(SeekIndexStatus.Error, "The seek index was patched but verification failed: " + verification.Detail);
        }

        return new InspectionResult(SeekIndexStatus.Fixed, "Retargeted the first SeekHead entry to Cues; " + cuesDescription)
        {
            CuesPosition = cuesPosition
        };
    }

    private static EbmlElement? FindFirstSeekHead(Stream stream, long segmentDataStart, long segmentDataEnd)
    {
        var position = segmentDataStart;
        while (position < segmentDataEnd)
        {
            stream.Position = position;
            if (!Ebml.TryReadElement(stream, out var element))
            {
                return null;
            }

            if (element.Id == Ebml.ClusterId)
            {
                return null;
            }

            if (element.Id == Ebml.SeekHeadId)
            {
                return element;
            }

            if (element.IsUnknownSize)
            {
                return null;
            }

            position = element.DataOffset + element.Size;
        }

        return null;
    }

    private static List<SeekEntry> ReadSeekEntries(Stream stream, EbmlElement seekHead)
    {
        var entries = new List<SeekEntry>();
        var end = seekHead.DataOffset + seekHead.Size;
        var position = seekHead.DataOffset;
        while (position < end)
        {
            stream.Position = position;
            if (!Ebml.TryReadElement(stream, out var element))
            {
                break;
            }

            if (element.Id == Ebml.SeekEntryId)
            {
                var entry = ReadSeekEntry(stream, element);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            if (element.IsUnknownSize)
            {
                break;
            }

            position = element.DataOffset + element.Size;
        }

        return entries;
    }

    private static SeekEntry? ReadSeekEntry(Stream stream, EbmlElement seekEntry)
    {
        byte[]? id = null;
        var positionValue = -1L;
        var idOffset = -1L;
        var idLength = 0;
        var positionOffset = -1L;
        var positionLength = 0;

        var end = seekEntry.DataOffset + seekEntry.Size;
        var position = seekEntry.DataOffset;
        Span<byte> positionBuffer = stackalloc byte[8];
        while (position < end)
        {
            stream.Position = position;
            if (!Ebml.TryReadElement(stream, out var element))
            {
                break;
            }

            if (element.Id == Ebml.SeekIdElementId && element.Size is > 0 and <= MaxSeekIdLength)
            {
                stream.Position = element.DataOffset;
                id = new byte[(int)element.Size];
                stream.ReadExactly(id);
                idOffset = element.DataOffset;
                idLength = id.Length;
            }
            else if (element.Id == Ebml.SeekPositionElementId && element.Size is > 0 and <= 8)
            {
                stream.Position = element.DataOffset;
                var length = (int)element.Size;
                stream.ReadExactly(positionBuffer[..length]);
                long value = 0;
                for (var i = 0; i < length; i++)
                {
                    value = (value << 8) | positionBuffer[i];
                }

                positionValue = value;
                positionOffset = element.DataOffset;
                positionLength = length;
            }

            if (element.IsUnknownSize)
            {
                break;
            }

            position = element.DataOffset + element.Size;
        }

        if (id is null || idOffset < 0 || positionValue < 0 || positionOffset < 0)
        {
            return null;
        }

        return new SeekEntry(id, positionValue, idOffset, idLength, positionOffset, positionLength);
    }

    private static (SeekEntry ChainEntry, long CuesPosition)? FindCuesThroughChain(
        Stream stream,
        long segmentDataStart,
        long fileLength,
        List<SeekEntry> entries)
    {
        var queue = new Queue<(SeekEntry Origin, long Position, int Depth)>();
        foreach (var entry in entries)
        {
            if (entry.ReferencesSeekHead)
            {
                queue.Enqueue((entry, entry.Position, 0));
            }
        }

        var visited = new HashSet<long>();
        while (queue.Count > 0)
        {
            var (origin, relativePosition, depth) = queue.Dequeue();
            var absolute = segmentDataStart + relativePosition;
            if (depth > MaxSeekHeadChainDepth || absolute <= 0 || absolute >= fileLength || !visited.Add(absolute))
            {
                continue;
            }

            stream.Position = absolute;
            if (!Ebml.TryReadElement(stream, out var seekHead) || seekHead.Id != Ebml.SeekHeadId)
            {
                continue;
            }

            foreach (var entry in ReadSeekEntries(stream, seekHead))
            {
                if (entry.ReferencesCues && IsCuesElementAt(stream, segmentDataStart, fileLength, entry.Position))
                {
                    return (origin, entry.Position);
                }

                if (entry.ReferencesSeekHead)
                {
                    queue.Enqueue((origin, entry.Position, depth + 1));
                }
            }
        }

        return null;
    }

    private static bool FitsIn(long value, int byteCount)
    {
        if (value < 0 || byteCount is <= 0 or > 8)
        {
            return false;
        }

        return byteCount == 8 || value < (1L << (byteCount * 8));
    }

    private static bool IsCuesElementAt(Stream stream, long segmentDataStart, long fileLength, long relativePosition)
    {
        var absolute = segmentDataStart + relativePosition;
        if (absolute < segmentDataStart || absolute >= fileLength)
        {
            return false;
        }

        stream.Position = absolute;
        return Ebml.TryReadElement(stream, out var element) && element.Id == Ebml.CuesId;
    }

    private static void WriteBigEndian(Stream stream, long value, int byteCount)
    {
        Span<byte> buffer = stackalloc byte[8];
        var remaining = value;
        for (var i = byteCount - 1; i >= 0; i--)
        {
            buffer[i] = (byte)(remaining & 0xFF);
            remaining >>= 8;
        }

        stream.Write(buffer[..byteCount]);
    }
}

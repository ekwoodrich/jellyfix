using System;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.JellyFix.Tests;

/// <summary>
/// A synthetic Matroska file plus the offsets it was built with.
/// </summary>
/// <param name="Bytes">The file content.</param>
/// <param name="CuesPosition">Position of the Cues element relative to the segment payload.</param>
/// <param name="SecondSeekHeadPosition">Position of the chained SeekHead relative to the segment payload.</param>
internal sealed record MockMatroska(byte[] Bytes, long CuesPosition, long SecondSeekHeadPosition);

/// <summary>
/// Builds minimal Matroska files that contain only the structural elements the inspector
/// cares about, so the repair logic can be tested without shipping media samples.
/// </summary>
internal static class MatroskaBuilder
{
    /// <summary>
    /// Builds a file whose first SeekHead only points at a second SeekHead (the defect).
    /// </summary>
    /// <param name="positionFieldLength">Width in bytes of the first SeekHead entry's SeekPosition field.</param>
    /// <param name="chainPositionFieldLength">Width of the chained SeekHead's SeekPosition field; 0 reuses the first.</param>
    /// <param name="padding">Extra bytes in the dummy cluster, used to push Cues further out.</param>
    /// <returns>The synthetic file.</returns>
    public static MockMatroska Broken(int positionFieldLength = 8, int chainPositionFieldLength = 0, int padding = 0)
    {
        if (chainPositionFieldLength <= 0)
        {
            chainPositionFieldLength = positionFieldLength;
        }

        var cluster = Element(0x1F43B675, new byte[64 + padding]);
        var cues = Element(0x1C53BB6B, new byte[16]);

        var firstSeekHead = SeekHead(SeekEntry(0x114D9B74, 0, positionFieldLength));
        var secondSeekHead = SeekHead(SeekEntry(0x1C53BB6B, 0, chainPositionFieldLength));

        var secondSeekHeadPosition = (long)firstSeekHead.Length + cluster.Length;
        var cuesPosition = secondSeekHeadPosition + secondSeekHead.Length;

        firstSeekHead = SeekHead(SeekEntry(0x114D9B74, secondSeekHeadPosition, positionFieldLength));
        secondSeekHead = SeekHead(SeekEntry(0x1C53BB6B, cuesPosition, chainPositionFieldLength));

        var file = Assemble(firstSeekHead, cluster, secondSeekHead, cues);
        return new MockMatroska(file, cuesPosition, secondSeekHeadPosition);
    }

    /// <summary>
    /// Builds a healthy file whose first SeekHead references Cues directly.
    /// </summary>
    /// <returns>The synthetic file.</returns>
    public static MockMatroska Healthy()
    {
        var firstSeekHead = SeekHead(SeekEntry(0x1C53BB6B, 0));
        var cluster = Element(0x1F43B675, new byte[64]);
        return new MockMatroska(Assemble(firstSeekHead, cluster), 0, 0);
    }

    /// <summary>
    /// Builds a file with a SeekHead that references something other than Cues.
    /// </summary>
    /// <returns>The synthetic file.</returns>
    public static MockMatroska WithoutCues()
    {
        var firstSeekHead = SeekHead(SeekEntry(0x1654AE6B, 0));
        var cluster = Element(0x1F43B675, new byte[64]);
        return new MockMatroska(Assemble(firstSeekHead, cluster), 0, 0);
    }

    private static byte[] Assemble(params byte[][] segmentChildren)
    {
        using var segmentPayload = new MemoryStream();
        foreach (var child in segmentChildren)
        {
            segmentPayload.Write(child);
        }

        using var file = new MemoryStream();
        file.Write(Element(0x1A45DFA3, [0x42, 0x86, 0x81, 0x01]));
        file.Write(Element(0x18538067, segmentPayload.ToArray()));
        return file.ToArray();
    }

    private static byte[] SeekHead(params byte[][] entries)
    {
        using var payload = new MemoryStream();
        foreach (var entry in entries)
        {
            payload.Write(entry);
        }

        return Element(0x114D9B74, payload.ToArray());
    }

    private static byte[] SeekEntry(uint targetId, long position, int positionFieldLength = 8)
    {
        using var payload = new MemoryStream();
        payload.Write(Element(0x53AB, IdBytes(targetId)));
        payload.Write(Element(0x53AC, BigEndian(position, positionFieldLength)));
        return Element(0x4DBB, payload.ToArray());
    }

    private static byte[] Element(uint id, byte[] payload)
    {
        using var buffer = new MemoryStream();
        buffer.Write(IdBytes(id));
        buffer.Write(SizeBytes(payload.Length));
        buffer.Write(payload);
        return buffer.ToArray();
    }

    private static byte[] IdBytes(uint id)
    {
        var bytes = new List<byte>();
        for (var shift = 24; shift >= 0; shift -= 8)
        {
            var value = (byte)((id >> shift) & 0xFF);
            if (bytes.Count == 0 && value == 0)
            {
                continue;
            }

            bytes.Add(value);
        }

        return [.. bytes];
    }

    private static byte[] SizeBytes(int size)
    {
        for (var length = 1; length <= 8; length++)
        {
            var max = (1L << (7 * length)) - 2;
            if (size <= max)
            {
                var value = (long)size | (1L << (7 * length));
                var bytes = new byte[length];
                for (var i = length - 1; i >= 0; i--)
                {
                    bytes[i] = (byte)(value & 0xFF);
                    value >>= 8;
                }

                return bytes;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(size));
    }

    private static byte[] BigEndian(long value, int length)
    {
        var bytes = new byte[length];
        var remaining = value;
        for (var i = length - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(remaining & 0xFF);
            remaining >>= 8;
        }

        return bytes;
    }
}

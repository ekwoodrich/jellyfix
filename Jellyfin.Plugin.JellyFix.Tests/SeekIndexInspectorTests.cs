using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.JellyFix.Matroska;
using Xunit;

namespace Jellyfin.Plugin.JellyFix.Tests;

public sealed class SeekIndexInspectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jellyfix-tests-" + Guid.NewGuid().ToString("N"));

    public SeekIndexInspectorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void DetectsRecursiveSeekHeadAsFixable()
    {
        var sample = MatroskaBuilder.Broken();
        var path = Write("broken.mkv", sample.Bytes);

        var result = SeekIndexInspector.Inspect(path, fix: false);

        Assert.Equal(SeekIndexStatus.Fixable, result.Status);
        Assert.Equal(sample.CuesPosition, result.CuesPosition);
    }

    [Fact]
    public void DryRunDoesNotModifyTheFile()
    {
        var sample = MatroskaBuilder.Broken();
        var path = Write("broken.mkv", sample.Bytes);

        SeekIndexInspector.Inspect(path, fix: false);

        Assert.Equal(sample.Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void RepairsRecursiveSeekHeadAndVerifies()
    {
        var path = Write("broken.mkv", MatroskaBuilder.Broken().Bytes);

        var fix = SeekIndexInspector.Inspect(path, fix: true);
        var verify = SeekIndexInspector.Inspect(path, fix: false);

        Assert.Equal(SeekIndexStatus.Fixed, fix.Status);
        Assert.Equal(SeekIndexStatus.Ok, verify.Status);
    }

    [Fact]
    public void RepairChangesOnlyTheSeekEntry()
    {
        var sample = MatroskaBuilder.Broken();
        var path = Write("broken.mkv", sample.Bytes);

        SeekIndexInspector.Inspect(path, fix: true);

        var patched = File.ReadAllBytes(path);
        Assert.Equal(sample.Bytes.Length, patched.Length);

        // Only the 4 byte SeekID and the low byte of the 8 byte SeekPosition actually differ.
        var changed = Enumerable.Range(0, sample.Bytes.Length)
            .Where(i => sample.Bytes[i] != patched[i])
            .ToList();
        Assert.Equal(5, changed.Count);
        Assert.All(changed, index => Assert.True(index < 48, "Patched bytes must stay inside the first SeekHead entry."));
    }

    [Fact]
    public void RepairIsIdempotent()
    {
        var path = Write("broken.mkv", MatroskaBuilder.Broken().Bytes);

        SeekIndexInspector.Inspect(path, fix: true);
        var second = SeekIndexInspector.Inspect(path, fix: true);

        Assert.Equal(SeekIndexStatus.Ok, second.Status);
    }

    [Fact]
    public void HealthyFileIsLeftAlone()
    {
        var sample = MatroskaBuilder.Healthy();
        var path = Write("healthy.mkv", sample.Bytes);

        var result = SeekIndexInspector.Inspect(path, fix: true);

        Assert.Equal(SeekIndexStatus.Ok, result.Status);
        Assert.Equal(sample.Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void SeekHeadWithoutCuesReferenceIsUnsupported()
    {
        var path = Write("no-cues.mkv", MatroskaBuilder.WithoutCues().Bytes);

        var result = SeekIndexInspector.Inspect(path, fix: true);

        Assert.Equal(SeekIndexStatus.Unsupported, result.Status);
    }

    [Fact]
    public void CuesPositionThatDoesNotFitIsUnsupported()
    {
        // Find a layout where the chained SeekHead position still fits in one byte but the
        // Cues position does not, which is the only case the repair has to refuse.
        MockMatroska? sample = null;
        for (var padding = 0; padding < 400 && sample is null; padding++)
        {
            var candidate = MatroskaBuilder.Broken(positionFieldLength: 1, chainPositionFieldLength: 8, padding: padding);
            if (candidate.SecondSeekHeadPosition <= 255 && candidate.CuesPosition > 255)
            {
                sample = candidate;
            }
        }

        Assert.NotNull(sample);
        var path = Write("narrow.mkv", sample!.Bytes);

        var dryRun = SeekIndexInspector.Inspect(path, fix: false);
        var fix = SeekIndexInspector.Inspect(path, fix: true);

        Assert.Equal(SeekIndexStatus.Fixable, dryRun.Status);
        Assert.Equal(SeekIndexStatus.Unsupported, fix.Status);
        Assert.Equal(sample.Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void NonMatroskaFileIsRejected()
    {
        var path = Path.Combine(_directory, "not-a-video.mkv");
        File.WriteAllText(path, "this is not a matroska file");

        var result = SeekIndexInspector.Inspect(path, fix: true);

        Assert.Equal(SeekIndexStatus.NotMatroska, result.Status);
    }

    [Fact]
    public void MissingFileIsReportedAsError()
    {
        var result = SeekIndexInspector.Inspect(Path.Combine(_directory, "missing.mkv"), fix: true);

        Assert.Equal(SeekIndexStatus.Error, result.Status);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

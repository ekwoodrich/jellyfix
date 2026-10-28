using System;
using System.IO;
using Jellyfin.Plugin.JellyFix.Matroska;
using Xunit;

namespace Jellyfin.Plugin.JellyFix.Tests;

/// <summary>
/// Optional smoke test against a real file. Set <c>JELLYFIX_SAMPLE</c> to the path of any
/// Matroska file to include it in the run; the file is only read, never written.
/// </summary>
public sealed class RealFileSmokeTests
{
    [Fact]
    public void RealSampleIsRecognised()
    {
        var sample = Environment.GetEnvironmentVariable("JELLYFIX_SAMPLE");
        if (string.IsNullOrEmpty(sample) || !File.Exists(sample))
        {
            return;
        }

        var result = SeekIndexInspector.Inspect(sample, fix: false);

        Assert.True(
            result.Status is SeekIndexStatus.Ok or SeekIndexStatus.Fixable,
            $"Unexpected status {result.Status}: {result.Detail}");
    }
}

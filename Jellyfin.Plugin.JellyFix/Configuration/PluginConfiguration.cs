using System;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyFix.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Enabled = true;
        FixAutomatically = true;
        RunAfterLibraryScan = false;
        SkipFilesInUse = true;
        ExcludedPathPrefixes = [];
    }

    /// <summary>
    /// Gets or sets a value indicating whether JellyFix does any work at all.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether broken files should actually be repaired.
    /// When <see langword="false"/>, JellyFix only reports what it finds (dry run).
    /// </summary>
    public bool FixAutomatically { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the repair also runs after every library scan.
    /// </summary>
    public bool RunAfterLibraryScan { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether files with an active playback session are skipped.
    /// </summary>
    public bool SkipFilesInUse { get; set; }

    /// <summary>
    /// Gets or sets path prefixes that are never touched, for example slow or read-only mounts.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required for XML plugin configuration serialization.")]
    public string[] ExcludedPathPrefixes { get; set; }

    /// <summary>
    /// Determines whether a path is excluded by <see cref="ExcludedPathPrefixes"/>.
    /// </summary>
    /// <param name="path">The file path to test.</param>
    /// <returns><see langword="true"/> if the path is excluded.</returns>
    public bool IsExcluded(string path)
    {
        if (ExcludedPathPrefixes is null || ExcludedPathPrefixes.Length == 0)
        {
            return false;
        }

        foreach (var prefix in ExcludedPathPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix)
                && path.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

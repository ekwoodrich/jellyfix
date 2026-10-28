using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyFix.Configuration;
using Jellyfin.Plugin.JellyFix.Matroska;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFix.Services;

/// <summary>
/// Finds Matroska files in the library and repairs their seek index.
/// </summary>
public sealed class SeekIndexFixService
{
    private static readonly string[] MatroskaExtensions = [".mkv", ".webm"];
    private static readonly BaseItemKind[] VideoItemKinds =
    [
        BaseItemKind.Movie,
        BaseItemKind.Episode,
        BaseItemKind.Video,
        BaseItemKind.MusicVideo,
        BaseItemKind.Trailer
    ];

    private readonly ILibraryManager _libraryManager;
    private readonly ISessionManager _sessionManager;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<SeekIndexFixService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeekIndexFixService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{SeekIndexFixService}"/> interface.</param>
    public SeekIndexFixService(
        ILibraryManager libraryManager,
        ISessionManager sessionManager,
        IApplicationPaths applicationPaths,
        ILogger<SeekIndexFixService> logger)
    {
        _libraryManager = libraryManager;
        _sessionManager = sessionManager;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full inspection pass over the library.
    /// </summary>
    /// <param name="afterLibraryScan">Whether this run was triggered by a finished library scan.</param>
    /// <param name="progress">Progress reporter, may be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the run.</returns>
    public async Task RunAsync(bool afterLibraryScan, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        if (!configuration.Enabled)
        {
            _logger.LogInformation("JellyFix is disabled in its settings, skipping.");
            return;
        }

        if (afterLibraryScan && !configuration.RunAfterLibraryScan)
        {
            _logger.LogDebug("JellyFix post-library-scan repair is disabled, skipping.");
            return;
        }

        await Task.Run(() => Run(configuration, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private void Run(PluginConfiguration configuration, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var files = GetCandidateFiles(configuration);
        _logger.LogInformation(
            "JellyFix: inspecting {FileCount} Matroska files (repair enabled: {RepairEnabled}).",
            files.Count,
            configuration.FixAutomatically);

        if (files.Count == 0)
        {
            return;
        }

        var counters = new Counters();
        using var report = new ReportWriter(_applicationPaths.DataPath, _logger);

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];

            if (configuration.SkipFilesInUse && IsInUse(path))
            {
                counters.Skipped++;
                _logger.LogDebug("JellyFix: skipping {Path} because it is currently being played.", path);
                continue;
            }

            if (!File.Exists(path))
            {
                counters.Missing++;
                _logger.LogDebug("JellyFix: skipping {Path} because it no longer exists (stale library entry).", path);
                continue;
            }

            var result = SeekIndexInspector.Inspect(path, configuration.FixAutomatically);
            RecordResult(result, path, counters, report);
            progress?.Report(100.0 * (index + 1) / files.Count);
        }

        _logger.LogInformation(
            "JellyFix finished: {Scanned} scanned, {Ok} already fine, {Fixed} repaired, {Fixable} repairable but not fixed, {Unfixable} not repairable, {Errors} errors, {Skipped} skipped, {Missing} missing.",
            files.Count,
            counters.Ok,
            counters.Fixed,
            counters.Fixable,
            counters.Unfixable,
            counters.Errors,
            counters.Skipped,
            counters.Missing);

        report.WriteSummary(counters, files.Count);
    }

    private void RecordResult(InspectionResult result, string path, Counters counters, ReportWriter report)
    {
        switch (result.Status)
        {
            case SeekIndexStatus.Ok:
                counters.Ok++;
                break;

            case SeekIndexStatus.Fixed:
                counters.Fixed++;
                _logger.LogInformation("JellyFix: repaired {Path} ({Detail})", path, result.Detail);
                report.Write("FIXED", path, result.Detail);
                break;

            case SeekIndexStatus.Fixable:
                counters.Fixable++;
                _logger.LogInformation("JellyFix: repairable but not modified in dry-run mode: {Path} ({Detail})", path, result.Detail);
                report.Write("FIXABLE", path, result.Detail);
                break;

            case SeekIndexStatus.NotMatroska:
                counters.Skipped++;
                _logger.LogDebug("JellyFix: skipping {Path} ({Detail})", path, result.Detail);
                break;

            case SeekIndexStatus.Unsupported:
                counters.Unfixable++;
                _logger.LogWarning("JellyFix: cannot repair {Path}: {Detail}", path, result.Detail);
                report.Write("UNFIXABLE", path, result.Detail);
                break;

            default:
                counters.Errors++;
                _logger.LogWarning("JellyFix: failed to inspect {Path}: {Detail}", path, result.Detail);
                report.Write("ERROR", path, result.Detail);
                break;
        }
    }

    private List<string> GetCandidateFiles(PluginConfiguration configuration)
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = VideoItemKinds
        };

        var items = _libraryManager.GetItemList(query);
        var files = new List<string>(items.Count);
        foreach (var item in items)
        {
            var path = item.Path;
            if (string.IsNullOrEmpty(path) || !IsMatroska(path) || configuration.IsExcluded(path))
            {
                continue;
            }

            files.Add(path);
        }

        return files;
    }

    private static bool IsMatroska(string path)
    {
        foreach (var extension in MatroskaExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInUse(string path)
    {
        foreach (var session in _sessionManager.Sessions)
        {
            var playingPath = session.NowPlayingItem?.Path;
            if (!string.IsNullOrEmpty(playingPath)
                && string.Equals(playingPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Counters
    {
        public int Ok { get; set; }

        public int Fixed { get; set; }

        public int Fixable { get; set; }

        public int Unfixable { get; set; }

        public int Errors { get; set; }

        public int Skipped { get; set; }

        public int Missing { get; set; }
    }

    /// <summary>
    /// Appends a plain text report next to the server data so results survive log rotation.
    /// </summary>
    private sealed class ReportWriter : IDisposable
    {
        private readonly StreamWriter? _writer;

        public ReportWriter(string dataPath, ILogger logger)
        {
            try
            {
                var directory = Path.Combine(dataPath, "jellyfix");
                Directory.CreateDirectory(directory);
                _writer = new StreamWriter(Path.Combine(directory, "report.log"), true);
                _writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "=== run started {0:O}", DateTime.UtcNow));
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "JellyFix: unable to open the report file, results will only be logged.");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "JellyFix: unable to open the report file, results will only be logged.");
            }
        }

        public void Write(string status, string path, string detail)
        {
            _writer?.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0:O}\t{1}\t{2}\t{3}",
                DateTime.UtcNow,
                status,
                path,
                detail));
        }

        public void WriteSummary(Counters counters, int scanned)
        {
            _writer?.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0:O}\tSUMMARY\tscanned={1}\tok={2}\tfixed={3}\tfixable={4}\tunfixable={5}\terrors={6}\tskipped={7}\tmissing={8}",
                DateTime.UtcNow,
                scanned,
                counters.Ok,
                counters.Fixed,
                counters.Fixable,
                counters.Unfixable,
                counters.Errors,
                counters.Skipped,
                counters.Missing));
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}

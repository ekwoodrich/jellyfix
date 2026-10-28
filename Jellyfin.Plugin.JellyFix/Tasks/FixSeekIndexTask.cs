using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyFix.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.JellyFix.Tasks;

/// <summary>
/// Scheduled task that inspects (and optionally repairs) Matroska seek indexes.
/// </summary>
public sealed class FixSeekIndexTask : IScheduledTask
{
    private readonly SeekIndexFixService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixSeekIndexTask"/> class.
    /// </summary>
    /// <param name="service">Instance of the <see cref="SeekIndexFixService"/> class.</param>
    public FixSeekIndexTask(SeekIndexFixService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public string Name => "Repair Matroska seek indexes";

    /// <inheritdoc />
    public string Key => "JellyFixSeekIndexRepair";

    /// <inheritdoc />
    public string Description =>
        "Finds Matroska files whose seek index cannot be reached by ExoPlayer-based clients and repairs them in place.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        ];
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RunAsync(false, progress, cancellationToken);
}

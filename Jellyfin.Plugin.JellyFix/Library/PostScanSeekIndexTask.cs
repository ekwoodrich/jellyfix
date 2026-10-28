using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyFix.Services;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyFix.Library;

/// <summary>
/// Runs the seek index repair right after a library scan, when enabled in the plugin settings.
/// </summary>
public sealed class PostScanSeekIndexTask : ILibraryPostScanTask
{
    private readonly SeekIndexFixService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostScanSeekIndexTask"/> class.
    /// </summary>
    /// <param name="service">Instance of the <see cref="SeekIndexFixService"/> class.</param>
    public PostScanSeekIndexTask(SeekIndexFixService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RunAsync(true, progress, cancellationToken);
}

namespace Jellyfin.Plugin.JellyFix.Matroska;

/// <summary>
/// Result of <see cref="SeekIndexInspector"/>.
/// </summary>
/// <param name="Status">The outcome of the inspection.</param>
/// <param name="Detail">Human readable explanation, suitable for logs and reports.</param>
internal sealed record InspectionResult(SeekIndexStatus Status, string Detail)
{
    /// <summary>
    /// Gets the position of the Cues element, relative to the segment payload, when known.
    /// </summary>
    public long CuesPosition { get; init; } = -1;
}

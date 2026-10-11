namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// Contributes extra segments to the status footer. <see cref="Segments"/> is called on each status
/// update; the segments append after the core fields in registration order. Presentation only:
/// the output never reaches the model or the run.
/// </summary>
public interface IStatusWidget
{
    /// <summary>Returns the segments for the current status; returning an empty list shows nothing.</summary>
    IReadOnlyList<TextSegment> Segments(StatusView view);
}

namespace Lunate.Agent;

/// <summary>
/// One user turn: the text the model sees and, optionally, the images attached to it. The text is
/// the processed prompt; each attachment becomes an image part behind an
/// <see cref="ImageContentReference"/> in the history.
/// </summary>
public sealed record UserInput(string Text, IReadOnlyList<ImageAttachment>? Images = null);

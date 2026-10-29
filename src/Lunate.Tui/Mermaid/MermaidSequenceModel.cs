namespace Lunate.Tui;

internal sealed record MermaidParticipant(string Id, string Name);

/// <summary>A statement inside a sequence diagram, in statement order.</summary>
internal abstract record MermaidSpeech;

internal sealed record MermaidNote(string Content, int[] Over) : MermaidSpeech;

internal sealed record MermaidMessage(int From, int To, string Content, bool Dashed)
    : MermaidSpeech;

internal sealed record MermaidSequenceModel(
    IReadOnlyList<MermaidParticipant> Participants,
    IReadOnlyList<MermaidSpeech> Speech
);

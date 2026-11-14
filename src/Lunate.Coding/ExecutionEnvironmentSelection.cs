using System.Globalization;

namespace Lunate.Coding;

/// <summary>
/// The one selection value a container environment is chosen by: <c>docker:&lt;image&gt;</c> or
/// <c>podman:&lt;image&gt;</c>, from <c>--env</c> or the user-scope <c>environment</c> setting. The
/// two engines differ only in the executable the runner starts, so one shape covers both. An image
/// may carry its own tag and registry path (<c>ghcr.io/org/image:tag</c>); anything else is refused
/// with the accepted shapes rather than falling back to the host.
/// </summary>
internal sealed record ExecutionEnvironmentSelection(string Engine, string Image)
{
    internal const string Docker = "docker";

    internal const string Podman = "podman";

    /// <summary>The two accepted shapes, as every refusal spells them.</summary>
    internal const string AcceptedShapes = "docker:<image> or podman:<image>";

    /// <summary>The selection as the header, <c>/context</c> and the settings value spell it.</summary>
    internal string Description => ToString();

    /// <summary>The engine executable the host half starts (<c>docker</c> or <c>podman</c>).</summary>
    internal string Executable => Engine;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Engine}:{Image}");

    /// <summary>
    /// Parses the one accepted shape. The image keeps everything after the first colon (tags and
    /// registry paths included); whitespace, a leading <c>-</c> and an empty engine or image are
    /// refused, so a value can never become engine arguments by accident.
    /// </summary>
    internal static bool TryParse(string? value, out ExecutionEnvironmentSelection? selection)
    {
        selection = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int colon = value.IndexOf(':');
        if (colon <= 0 || colon == value.Length - 1)
        {
            return false;
        }

        string engine = value[..colon];
        string image = value[(colon + 1)..];
        if (
            engine is not (Docker or Podman)
            || string.IsNullOrWhiteSpace(image)
            || image.StartsWith('-')
            || image.Any(char.IsWhiteSpace)
        )
        {
            return false;
        }

        selection = new ExecutionEnvironmentSelection(engine, image);
        return true;
    }
}

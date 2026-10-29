using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Lunate.Ai;
using Lunate.Extensibility;

namespace Lunate.Coding;

/// <summary>One event kept for the bundle: when it arrived on the session clock and its summary.</summary>
internal sealed record DiagnosticEvent(DateTimeOffset At, string Text);

/// <summary>
/// Everything one <c>/debug</c> run reports. Paths and identifiers only: no prompt text, no tool
/// output, no command arguments and no file contents from the state root. The credential
/// redaction is applied to the rendered document, not to each field.
/// </summary>
internal sealed record DiagnosticBundleInput
{
    public required string Destination { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public string? SessionPath { get; init; }

    public string? SessionId { get; init; }

    public string? SessionTitle { get; init; }

    public int SessionEntries { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? GitBranch { get; init; }

    public required string StateRoot { get; init; }

    public required bool HomeConfigured { get; init; }

    public required AgentSettings Settings { get; init; }

    public required IReadOnlyList<string> AuthKeys { get; init; }

    public required ModelInfo Model { get; init; }

    /// <summary>Where the session model came from: <c>catalog</c> or <c>extension</c>.</summary>
    public required string ModelSource { get; init; }

    public string? ModelRoute { get; init; }

    public required ThinkingLevel Thinking { get; init; }

    public required string ExtensionsText { get; init; }

    public IReadOnlyList<ExtensionSessionError> ExtensionErrors { get; init; } = [];

    public IReadOnlyList<SingleFileExtension> SingleFileExtensions { get; init; } = [];

    /// <summary>The events the bundle lists, oldest first.</summary>
    public IReadOnlyList<DiagnosticEvent> Events { get; init; } = [];

    /// <summary>How many events the session has recorded, including the ones trimmed from the ring.</summary>
    public int RecordedEvents { get; init; }
}

/// <summary>
/// The <c>/debug</c> bundle: one UTF-8 (no BOM, LF) text file a reporter can attach to an issue
/// without explaining their setup. It carries the version, the effective configuration, the
/// resolved model, the extension graph with its trust digests, the state root's path layout (never
/// its contents) and the last <see cref="EventLimit"/> events with their timings. The destination
/// defaults to <c>&lt;state root&gt;/diagnostics</c> — outside the workspace, where it cannot be
/// committed by accident.
/// </summary>
internal static partial class DiagnosticBundle
{
    /// <summary>The directory name under the state root.</summary>
    internal const string DirectoryName = "diagnostics";

    /// <summary>How many recent events one bundle lists.</summary>
    internal const int EventLimit = 64;

    /// <summary>How many events the session keeps in memory for the bundle.</summary>
    internal const int KeptEventLimit = 256;

    /// <summary>The longest error or reason text one event line carries.</summary>
    internal const int MaxDetailLength = 200;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The absolute destination for one <c>/debug</c> request. Without an argument the file is
    /// <c>&lt;state root&gt;/diagnostics/&lt;UTC stamp&gt;-&lt;session id&gt;.txt</c>; a relative
    /// argument resolves against the session's working directory.
    /// </summary>
    internal static string Resolve(
        string? argument,
        string storeRoot,
        string workingDirectory,
        string? sessionId,
        DateTimeOffset now
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!string.IsNullOrWhiteSpace(argument))
        {
            string anchored = Path.IsPathRooted(argument)
                ? argument
                : Path.Combine(workingDirectory, argument);
            return Path.GetFullPath(anchored);
        }

        string stamp = now.ToUniversalTime()
            .ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string name = ExportTargets.FileName(sessionId ?? "session");
        return Path.GetFullPath(
            Path.Combine(storeRoot, DirectoryName, stamp + "-" + name + ".txt")
        );
    }

    /// <summary>
    /// Renders the bundle, applies the session's exact-value credential redaction, and writes it
    /// owner-only on POSIX. The full document is built before the file is opened, so a rendering
    /// failure never leaves a partial bundle behind.
    /// </summary>
    internal static string Write(DiagnosticBundleInput input, CredentialRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(redactor);

        string text = redactor.Redact(Render(input));
        string? directory = Path.GetDirectoryName(input.Destination);
        if (!string.IsNullOrEmpty(directory))
        {
            SecureFiles.CreateOwnerOnlyDirectory(directory);
        }

        using (
            FileStream stream = SecureFiles.OpenOwnerOnly(
                input.Destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            )
        )
        using (var writer = new StreamWriter(stream, Utf8NoBom))
        {
            writer.Write(text);
        }

        SecureFiles.RestrictToOwner(input.Destination);
        return input.Destination;
    }

    /// <summary>The one document. Newlines are always LF so the bytes do not depend on the platform.</summary>
    internal static string Render(DiagnosticBundleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var text = new StringBuilder();
        text.Line("lunate diagnostic bundle");
        text.Line("generated: " + input.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        text.Line("version: " + ProductVersion.Informational);
        text.Line("runtime: " + RuntimeInformation.FrameworkDescription);
        text.Line(
            "platform: "
                + RuntimeInformation.OSDescription
                + " ("
                + RuntimeInformation.ProcessArchitecture
                + ")"
        );
        text.Line();
        text.Line(
            "A snapshot for a bug report. Every credential value Lunate knows is replaced with"
        );
        text.Line(
            "[redacted:<name>] before the file is written; a value pasted into a prompt is not"
        );
        text.Line("known and is not replaced. Review the file before sharing it.");

        AppendSession(text, input);
        AppendStateRoot(text, input);
        AppendConfig(text, input);
        AppendModel(text, input);
        AppendExtensions(text, input);
        AppendEvents(text, input);
        return text.ToString();
    }

    /// <summary>Appends one LF-terminated line, never the platform's newline.</summary>
    private static void Line(this StringBuilder text, string value = "") =>
        text.Append(value).Append('\n');

    private static void Section(StringBuilder text, string name)
    {
        text.Line();
        text.Line("== " + name + " ==");
    }

    private static string Value(string? value) => string.IsNullOrEmpty(value) ? "(none)" : value;

    /// <summary>
    /// A scheme, host, port and path. Userinfo and query are dropped, so a credential embedded in a
    /// configured endpoint never reaches the bundle.
    /// </summary>
    private static string Endpoint(Uri? endpoint) =>
        endpoint is null
            ? "(default)"
            : endpoint.Scheme + "://" + endpoint.Authority + endpoint.AbsolutePath;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";

    private static string Number(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";

    private static string Lower(bool value) => value ? "true" : "false";

    private static string Lower(Enum value) => value.ToString().ToLowerInvariant();

    private static string Truncate(string text)
    {
        string oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= MaxDetailLength ? oneLine : oneLine[..MaxDetailLength] + "…";
    }
}

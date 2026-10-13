using Lunate.Agent;

namespace CodeModeExtension;

/// <summary>
/// Composes the temp script file: the embedded runtime footer with the generated stub prelude and
/// the model's code injected. The runtime is embedded, never generated; only the stubs and the
/// script are rendered in.
/// </summary>
internal static class CodeModeScript
{
    private const string RuntimeResourceName = "CodeModeExtension.CodeModeRuntime.js";

    private static readonly string Runtime = LoadRuntime();

    public static string Compose(IReadOnlyList<ToolStub> stubs, string code) =>
        Runtime
            .Replace("// __LUNATE_STUBS__", CodeModeStubs.Render(stubs), StringComparison.Ordinal)
            .Replace("// __LUNATE_SCRIPT__", code, StringComparison.Ordinal);

    private static string LoadRuntime()
    {
        using Stream stream =
            typeof(CodeModeScript).Assembly.GetManifestResourceStream(RuntimeResourceName)
            ?? throw new InvalidOperationException(
                $"the embedded code-mode runtime '{RuntimeResourceName}' is missing from the extension assembly"
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

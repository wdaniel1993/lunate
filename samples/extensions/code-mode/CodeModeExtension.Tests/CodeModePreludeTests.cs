using System.Text.Json;
using CodeModeExtension;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace CodeModeExtension.Tests;

/// <summary>
/// The sample's pure units: the JSDoc prelude rendering, the runtime composition (embedded footer,
/// injected stubs and script) and the settings defaults and clamps.
/// </summary>
public sealed class CodeModePreludeTests
{
    [Fact]
    public void The_prelude_documents_tools_and_parameters_with_jsdoc()
    {
        var stubs = new ToolStub[]
        {
            new(
                "read",
                "Reads a file.",
                [
                    new ToolStubParameter("path", "string", true, "the path", null, null, null),
                    new ToolStubParameter(
                        "limit",
                        "integer",
                        false,
                        "the maximum",
                        "10",
                        null,
                        null
                    ),
                    new ToolStubParameter(
                        "filter",
                        "any",
                        false,
                        "the filter",
                        null,
                        null,
                        "unsupported schema construct: oneOf"
                    ),
                ],
                HasStructuredOutput: true
            ),
        };

        string prelude = CodeModeStubs.Render(stubs);

        Assert.Contains("/**", prelude, StringComparison.Ordinal);
        Assert.Contains(" * Reads a file.", prelude, StringComparison.Ordinal);
        Assert.Contains("- path (string, required): the path", prelude, StringComparison.Ordinal);
        Assert.Contains(
            "- limit (integer, optional): the maximum; default: 10",
            prelude,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "note: unsupported schema construct: oneOf",
            prelude,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "the tool also produces structured content",
            prelude,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "tools[\"read\"] = async (args) => __call(\"read\", args);",
            prelude,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_prelude_never_lets_a_description_close_the_comment()
    {
        var stubs = new ToolStub[]
        {
            new("odd", "Ends the comment */ here.", [], HasStructuredOutput: false),
        };

        string prelude = CodeModeStubs.Render(stubs);

        Assert.DoesNotContain("*/ here", prelude, StringComparison.Ordinal);
        Assert.Contains("*\\/ here", prelude, StringComparison.Ordinal);
    }

    [Fact]
    public void The_composed_script_joins_the_embedded_runtime_stubs_and_code()
    {
        var stubs = new ToolStub[] { new("read", "Reads.", [], HasStructuredOutput: false) };

        string script = CodeModeScript.Compose(stubs, "return \"answer\";");

        Assert.Contains("protocol: 1", script, StringComparison.Ordinal);
        Assert.Contains("__call(\"read\", args)", script, StringComparison.Ordinal);
        Assert.Contains("return \"answer\";", script, StringComparison.Ordinal);
        Assert.DoesNotContain("__LUNATE_STUBS__", script, StringComparison.Ordinal);
        Assert.DoesNotContain("__LUNATE_SCRIPT__", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_default_and_clamp()
    {
        CodeModeSettings defaults = CodeModeSettings.From(new DictionarySettings([]));

        Assert.Equal("node", defaults.Runtime);
        Assert.Equal(30, defaults.TimeoutSeconds);
        Assert.Equal(65536, defaults.MaxOutputBytes);
        Assert.Equal(100, defaults.MaxToolCalls);

        CodeModeSettings configured = CodeModeSettings.From(
            new DictionarySettings(
                new Dictionary<string, object>
                {
                    ["runtime"] = "/opt/node",
                    ["timeoutSeconds"] = 1000,
                    ["maxOutputBytes"] = 0,
                    ["maxToolCalls"] = -3,
                }
            )
        );

        Assert.Equal("/opt/node", configured.Runtime);
        Assert.Equal(600, configured.TimeoutSeconds);
        Assert.Equal(65536, configured.MaxOutputBytes);
        Assert.Equal(100, configured.MaxToolCalls);
    }

    [Fact]
    public void A_call_may_only_tighten_the_configured_timeout()
    {
        var settings = new CodeModeSettings("node", 30, 65536, 100);

        Assert.Equal(5, settings.ClampTimeout(5));
        Assert.Equal(30, settings.ClampTimeout(600));
        Assert.Equal(1, settings.ClampTimeout(0));
    }

    private sealed class DictionarySettings(Dictionary<string, object> values) : IExtensionSettings
    {
        public bool TryGet(string path, out JsonElement value)
        {
            if (values.TryGetValue(path, out object? raw))
            {
                value = JsonSerializer.SerializeToElement(raw);
                return true;
            }

            value = default;
            return false;
        }
    }
}

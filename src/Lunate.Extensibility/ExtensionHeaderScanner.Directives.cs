namespace Lunate.Extensibility;

public static partial class ExtensionHeaderScanner
{
    /// <summary>
    /// The mutable scan state: the declaration lists being built, the diagnostics and the one
    /// required directive. One instance per <see cref="Scan"/> call, so the public scanner stays
    /// stateless.
    /// </summary>
    private sealed class Builder
    {
        private readonly List<string> _tools = [];
        private readonly Dictionary<string, int> _toolLines = new(StringComparer.Ordinal);
        private readonly List<string> _commands = [];
        private readonly List<string> _hooks = [];
        private readonly List<string> _services = [];
        private readonly List<string> _capabilities = [];
        private readonly List<string> _providers = [];
        private readonly List<Abstractions.WriteVerifierDeclaration> _verifiers = [];
        private readonly List<ExtensionPackageReference> _packages = [];
        private string? _id;
        private string? _version;
        private int _extensionLine;
        private bool _generators;

        internal List<ExtensionHeaderIssue> Errors { get; } = [];

        internal List<ExtensionHeaderIssue> Warnings { get; } = [];

        internal ExtensionHeader Build()
        {
            ValidateToolNames();
            return new ExtensionHeader
            {
                Id = _id,
                Version = _version,
                Tools = _tools,
                Commands = _commands,
                Hooks = _hooks,
                Services = _services,
                Capabilities = _capabilities,
                Providers = _providers,
                WriteVerifiers = _verifiers,
                Packages = _packages,
                Generators = _generators,
                Errors = Errors,
                Warnings = Warnings,
            };
        }

        /// <summary>Applies one leading-block directive; <paramref name="body"/> has no <c>#:</c>.</summary>
        internal void Apply(string body, int line)
        {
            string text = body.Trim();
            var split = 0;
            while (split < text.Length && !char.IsWhiteSpace(text[split]))
            {
                split++;
            }

            string name = text[..split];
            string value = text[split..].Trim();
            switch (name)
            {
                case "":
                    Error(line, "a '#:' line needs a directive name, for example '#:tool <name>'.");
                    return;
                case ExtensionDirectives.Extension:
                    Extension(value, line);
                    return;
                case ExtensionDirectives.Tool:
                    int before = _tools.Count;
                    Declaration(_tools, "#:tool", value, line);
                    if (_tools.Count > before)
                    {
                        _toolLines[value] = line;
                    }

                    return;
                case ExtensionDirectives.Command:
                    Declaration(_commands, "#:command", value, line);
                    return;
                case ExtensionDirectives.Hook:
                    Declaration(_hooks, "#:hook", value, line);
                    return;
                case ExtensionDirectives.Service:
                    Declaration(_services, "#:service", value, line);
                    return;
                case ExtensionDirectives.Capability:
                    Declaration(_capabilities, "#:capability", value, line);
                    return;
                case ExtensionDirectives.Provider:
                    Provider(value, line);
                    return;
                case ExtensionDirectives.Verifier:
                    Verifier(value, line);
                    return;
                case ExtensionDirectives.Package:
                    Package(value, line);
                    return;
                case ExtensionDirectives.Generators:
                    Generators(value, line);
                    return;
                case "sdk" or "property" or "project":
                    string refused = "#:" + name;
                    Error(
                        line,
                        $"'{refused}' is not supported in a Lunate single-file extension: Lunate "
                            + "owns the project and the restore, so the header declares what the file "
                            + "provides ('#:tool', '#:hook', …) and the packages it needs "
                            + "('#:package'). Remove the line."
                    );
                    return;
                default:
                    Warnings.Add(
                        new ExtensionHeaderIssue(
                            line,
                            $"unknown directive '#:{name}' ignored; a single-file extension header "
                                + "accepts #:extension, #:tool, #:command, #:hook, #:service, "
                                + "#:capability, #:provider, #:verifier, #:package and "
                                + "#:generators."
                        )
                    );
                    return;
            }
        }

        private void Extension(string value, int line)
        {
            if (_id is not null)
            {
                Error(
                    line,
                    $"'{_id}' is already declared by the '#:extension' on line "
                        + FormattableString.Invariant($"{_extensionLine}")
                        + "; a file declares exactly one extension. Remove the extra '#:extension' "
                        + "line."
                );
                return;
            }

            if (!SplitPair(value, "#:extension", line, out string id, out string version))
            {
                return;
            }

            if (!ExtensionGrammar.IsValidExtensionId(id))
            {
                Error(
                    line,
                    $"'#:extension' id '{id}' must match "
                        + Abstractions.ExtensionIds.Pattern
                        + " (lowercase letters, digits and hyphens), because it names the "
                        + "extension's tools and settings and a package would refuse the same id."
                );
                return;
            }

            if (!ExtensionGrammar.IsExactVersion(version))
            {
                Error(
                    line,
                    $"'#:extension' version '{version}' must be one exact version such as '1.0.0'; "
                        + "a range belongs to '#:package'."
                );
                return;
            }

            _id = id;
            _version = version;
            _extensionLine = line;
        }

        private void Declaration(List<string> names, string directive, string value, int line)
        {
            if (value.Length == 0)
            {
                Error(line, $"'{directive}' needs a name, for example '{directive} <name>'.");
                return;
            }

            if (value.Any(char.IsWhiteSpace))
            {
                Error(
                    line,
                    $"'{directive}' takes one name; '{directive} {value}' has more than one."
                );
                return;
            }

            if (names.Contains(value, StringComparer.Ordinal))
            {
                Error(line, $"'{directive}' declares '{value}' more than once.");
                return;
            }

            names.Add(value);
        }

        private void Provider(string value, int line)
        {
            if (value.Length == 0 || value.Any(char.IsWhiteSpace))
            {
                Error(
                    line,
                    "'#:provider' needs one provider id, for example '#:provider acme-models'."
                );
                return;
            }

            if (!ExtensionGrammar.IsValidProviderId(value))
            {
                Error(
                    line,
                    $"'#:provider' id '{value}' must match [a-z0-9-]+, like the manifest's "
                        + "'modelProviders[].id'."
                );
                return;
            }

            if (_providers.Contains(value, StringComparer.Ordinal))
            {
                Error(line, $"'#:provider' declares '{value}' more than once.");
                return;
            }

            _providers.Add(value);
        }

        /// <summary>
        /// Reads one verifier as <c>&lt;name&gt; &lt;pattern&gt;…</c>: the name is one token and the
        /// rest are the filename globs it handles. The patterns follow the manifest's rule —
        /// non-empty, no path separator, no duplicate within a verifier.
        /// </summary>
        private void Verifier(string value, int line)
        {
            string[] parts = value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length < 2)
            {
                Error(
                    line,
                    "'#:verifier' needs a name and at least one filename pattern, for example "
                        + "'#:verifier csharp *.cs'."
                );
                return;
            }

            string name = parts[0];
            if (
                _verifiers.Any(verifier =>
                    string.Equals(verifier.Name, name, StringComparison.Ordinal)
                )
            )
            {
                Error(line, $"'#:verifier' declares '{name}' more than once.");
                return;
            }

            List<string> patterns = [];
            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            foreach (string pattern in parts.Skip(1))
            {
                if (pattern.Contains('/') || pattern.Contains('\\'))
                {
                    Error(
                        line,
                        $"'#:verifier' pattern '{pattern}' must not contain a path separator."
                    );
                    return;
                }

                if (!unique.Add(pattern))
                {
                    Error(line, $"'#:verifier' pattern '{pattern}' is duplicated.");
                    return;
                }

                patterns.Add(pattern);
            }

            _verifiers.Add(new Abstractions.WriteVerifierDeclaration(name, patterns));
        }

        private void Package(string value, int line)
        {
            if (!SplitPair(value, "#:package", line, out string id, out string range))
            {
                return;
            }

            if (!ExtensionGrammar.IsValidPackageId(id))
            {
                Error(
                    line,
                    $"'#:package' id '{id}' must be a package id: letters, digits, '.', '_' and '-', "
                        + "starting with a letter or digit."
                );
                return;
            }

            if (!ExtensionGrammar.IsVersionRange(range))
            {
                Error(
                    line,
                    $"'#:package' version '{range}' is not a version, a range or a floating "
                        + "version; write 'Octokit@14.0.0', 'Octokit@[14.0.0,15.0.0)' or "
                        + "'Octokit@14.*'."
                );
                return;
            }

            if (
                _packages.Any(package =>
                    string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                Error(
                    line,
                    $"'#:package' declares package '{id}' more than once; declare one version or "
                        + "range per package."
                );
                return;
            }

            _packages.Add(new ExtensionPackageReference(id, range, $"{id}@{range}"));
        }

        private void Generators(string value, int line)
        {
            if (!string.Equals(value, "on", StringComparison.Ordinal))
            {
                Error(
                    line,
                    $"'#:generators' takes 'on'; '{value}' is not recognised. Build-time analyzers "
                        + "and generators run only when the header says '#:generators on'."
                );
                return;
            }

            _generators = true;
        }

        /// <summary>
        /// Splits <c>&lt;id&gt;@&lt;version&gt;</c> and insists on one <c>@</c> with no space
        /// around it, so the argument can be recorded exactly as the author wrote it.
        /// </summary>
        private bool SplitPair(
            string value,
            string directive,
            int line,
            out string id,
            out string version
        )
        {
            id = string.Empty;
            version = string.Empty;
            int at = value.IndexOf('@', StringComparison.Ordinal);
            if (at <= 0 || at == value.Length - 1)
            {
                string example = directive == "#:package" ? "Octokit@14.0.0" : "acme-tools@1.0.0";
                Error(
                    line,
                    $"'{directive}' needs '<id>@<version>', for example '{directive} {example}'."
                );
                return false;
            }

            if (char.IsWhiteSpace(value[at - 1]) || char.IsWhiteSpace(value[at + 1]))
            {
                Error(line, $"'{directive}' takes '<id>@<version>' with no space around '@'.");
                return false;
            }

            id = value[..at].Trim();
            version = value[(at + 1)..].Trim();
            return true;
        }

        /// <summary>
        /// Refuses a declared tool the provider could not accept: a local name outside
        /// <c>[A-Za-z0-9_-]</c>, or a composed <c>&lt;id&gt;__&lt;tool&gt;</c> longer than 64
        /// characters. Both name the tool and, for the length, the exact name that is too long, so
        /// the author sees whether the id or the name has to shrink.
        /// </summary>
        private void ValidateToolNames()
        {
            if (_id is null)
            {
                return;
            }

            foreach (string tool in _tools)
            {
                if (!Abstractions.ExtensionToolNaming.IsValidLocalName(tool))
                {
                    Error(
                        _toolLines[tool],
                        $"tool name '{tool}' must match [A-Za-z0-9_-]; provider APIs refuse other "
                            + "characters."
                    );
                    continue;
                }

                string modelFacing = Abstractions.ExtensionToolNaming.ModelFacing(_id, tool);
                if (modelFacing.Length > Abstractions.ExtensionToolNaming.MaxModelFacingNameLength)
                {
                    Error(
                        _toolLines[tool],
                        FormattableString.Invariant(
                            $"tool '{tool}' in extension '{_id}' makes the model-facing name '{modelFacing}' ({modelFacing.Length} characters), over the provider limit of {Abstractions.ExtensionToolNaming.MaxModelFacingNameLength}; shorten the tool name or the extension id."
                        )
                    );
                }
            }
        }

        private void Error(int line, string message) =>
            Errors.Add(new ExtensionHeaderIssue(line, message));
    }
}

using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>Injected paths and boundaries for <see cref="AuthCommand"/>; null means the production default.</summary>
internal sealed record AuthCommandSeams(
    string? AuthPath = null,
    string? SettingsPath = null,
    string? ModelsPath = null,
    Func<string, string?>? Environment = null,
    TextReader? Input = null,
    ISecretPrompt? SecretPrompt = null,
    bool? StdinIsTerminal = null,
    HttpMessageHandler? ProviderProbeHandler = null,
    TimeSpan? ProviderProbeTimeout = null
);

/// <summary>
/// The <c>lunate auth</c> command: list, login, logout and status without the TUI and without an
/// unbounded network wait. Every path reports names and sources, never a credential value; a key read from a
/// pipe is the same secret as one typed at the masked prompt, so neither is echoed. Exit codes: 0
/// success, 1 the command failed or nothing is configured, 2 usage.
/// </summary>
internal static partial class AuthCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage = "Usage: lunate auth <list|login|logout|status>";

    internal const string LoginUsage =
        "Usage: lunate auth login <provider> [--key-stdin]\n"
        + "       lunate auth login --custom [--id <id> --base-url <url> --model <model>] [--key-stdin]";

    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter errors,
        AuthCommandSeams? seams = null
    ) => new Runner(output, errors, seams ?? new AuthCommandSeams()).Run(args);

    private sealed partial class Runner(
        TextWriter output,
        TextWriter errors,
        AuthCommandSeams seams
    )
    {
        public int Run(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    return UsageError();
                }

                return args[0] switch
                {
                    "list" => List(args[1..]),
                    "login" => Login(args[1..]),
                    "logout" => Logout(args[1..]),
                    "status" => Status(args[1..]),
                    _ => UsageError(),
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Load errors name keys and paths, never values; a malformed auth file fails here.
                errors.WriteLine($"lunate: {exception.Message}");
                return ExitFailure;
            }
        }

        private int List(string[] args)
        {
            if (args.Length > 0)
            {
                return UsageError();
            }

            AuthStore auth = AuthStore.Load(seams.AuthPath, Environment);
            AgentSettings settings = SettingsStore.Resolve(
                seams.SettingsPath ?? AuthStore.SettingsFilePath(seams.AuthPath),
                _ => null
            );
            ProviderStore providers = ProviderStore.Load(
                AuthStore.ProviderStoreFilePath(seams.AuthPath),
                auth,
                settings.Providers
            );
            var rows =
                new List<(string Name, string Source, string Credential, string Reachability)>();
            using var client = new HttpClient(
                seams.ProviderProbeHandler ?? new SocketsHttpHandler(),
                disposeHandler: seams.ProviderProbeHandler is null
            )
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            foreach (ProviderPreset preset in ProviderPresets.All)
            {
                if (providers.Entries.FirstOrDefault(entry => entry.Name == preset.Id) is { } entry)
                {
                    rows.Add(ProviderRow(entry, auth, client));
                    continue;
                }

                string? variable = Variable(preset, Environment);
                string source =
                    variable is not null ? $"environment ({variable})"
                    : auth.Keys.ContainsKey(preset.Id) ? "stored"
                    : "not set";
                string credential = source == "not set" ? "absent" : "present";
                rows.Add((preset.Id, source, credential, "unknown"));
            }

            foreach (
                ProviderEntry entry in providers
                    .Entries.Where(entry => ProviderPresets.Find(entry.Name) is null)
                    .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            )
            {
                rows.Add(ProviderRow(entry, auth, client));
            }

            int width = rows.Max(row => row.Name.Length);
            foreach ((string name, string source, string credential, string reachability) in rows)
            {
                output.WriteLine(
                    $"{name.PadRight(width)}  {source}; credential: {credential}; reachability: {reachability}"
                );
            }

            return ExitOk;
        }

        private int Logout(string[] args)
        {
            if (args.Length != 1 || args[0].StartsWith('-'))
            {
                return UsageError();
            }

            string token = args[0];
            // Unlike login, logout names what it could not find: the token is a provider, not a key.
            if (ProviderPresets.Find(token) is not { } preset)
            {
                errors.WriteLine($"unknown provider '{token}'; providers: {Supported}");
                return ExitUsage;
            }

            AuthStore auth = AuthStore.Load(seams.AuthPath, Environment);
            string? variable = Variable(preset, Environment);
            string suffix = variable is null
                ? string.Empty
                : $"; {variable} is still set in the environment";
            if (!auth.Keys.TryGetValue(preset.Id, out string? previous))
            {
                errors.WriteLine($"auth: no stored key for '{preset.Id}'{suffix}");
                return ExitFailure;
            }

            auth.Remove(preset.Id);
            try
            {
                auth.Save();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                auth.Set(preset.Id, previous);
                errors.WriteLine($"lunate: could not save the auth file: {exception.Message}");
                return ExitFailure;
            }

            output.WriteLine($"auth: removed the stored key for '{preset.Id}'{suffix}");
            return ExitOk;
        }

        private int Status(string[] args)
        {
            if (args.Length > 0)
            {
                return UsageError();
            }

            AgentSettings settings = SettingsStore.Resolve(seams.SettingsPath, Environment);
            if (string.IsNullOrWhiteSpace(settings.Model))
            {
                output.WriteLine("default provider: none (no model is configured)");
                output.WriteLine("credential: missing");
                return ExitFailure;
            }

            ModelInfo model = HarnessFactory.ResolveModel(
                settings.Model,
                HarnessFactory.LoadCatalog(settings, seams.ModelsPath)
            );
            output.WriteLine($"default provider: {model.Provider}");

            AuthStore auth = AuthStore.Load(seams.AuthPath, Environment);
            if (!string.IsNullOrWhiteSpace(model.AuthRef))
            {
                if (auth.TryGet(model.AuthRef) is not null)
                {
                    output.WriteLine("credential: stored");
                    return ExitOk;
                }

                output.WriteLine("credential: missing");
                return ExitFailure;
            }

            string? variable = ProviderPresets.Find(model.Provider) is { } preset
                ? Variable(preset, Environment)
                : null;
            if (variable is not null)
            {
                output.WriteLine($"credential: environment ({variable})");
                return ExitOk;
            }

            if (auth.TryGet(model.Provider) is not null)
            {
                output.WriteLine("credential: stored");
                return ExitOk;
            }

            output.WriteLine("credential: missing");
            return ExitFailure;
        }

        private Func<string, string?> Environment =>
            seams.Environment ?? System.Environment.GetEnvironmentVariable;

        private (string Name, string Source, string Credential, string Reachability) ProviderRow(
            ProviderEntry entry,
            AuthStore auth,
            HttpClient client
        )
        {
            (string credential, string source) = CredentialStatus(entry, auth);
            string name = ProviderPresets.Find(entry.Name) is null
                ? $"{entry.Name} ({entry.Kind})"
                : entry.Name;
            ProviderReachability reachability = ProviderReachabilityProbe.Check(
                client,
                entry.BaseUrl,
                seams.ProviderProbeTimeout ?? TimeSpan.FromSeconds(2)
            );
            return (name, source, credential, reachability.ToString().ToLowerInvariant());
        }

        private (string Credential, string Source) CredentialStatus(
            ProviderEntry entry,
            AuthStore auth
        )
        {
            string reference = entry.CredentialReference;
            if (reference.StartsWith("auth:", StringComparison.Ordinal))
            {
                string key = reference["auth:".Length..];
                if (
                    ProviderPresets.Find(key) is { } preset
                    && Variable(preset, Environment) is { } variable
                )
                {
                    return ("present", $"environment ({variable})");
                }

                return auth.TryGet(key) is null ? ("absent", "not set") : ("present", "stored");
            }

            if (reference.StartsWith("env:", StringComparison.Ordinal))
            {
                string variable = reference["env:".Length..];
                return string.IsNullOrWhiteSpace(Environment(variable))
                    ? ("absent", "not set")
                    : ("present", $"environment ({variable})");
            }

            // A helper reference is accepted by the store schema but no host can resolve one in
            // 0.2: list must not claim a credential is present that nothing can produce.
            return ("absent", "helper unsupported");
        }

        private bool IsTerminal => seams.StdinIsTerminal ?? !Console.IsInputRedirected;

        private static string Supported =>
            string.Join(", ", ProviderPresets.All.Select(preset => preset.Id));

        private static string? Variable(ProviderPreset preset, Func<string, string?> environment) =>
            !string.IsNullOrWhiteSpace(environment(preset.KeyVariable)) ? preset.KeyVariable
            : preset.KeyVariableAlias is { } alias && !string.IsNullOrWhiteSpace(environment(alias))
                ? alias
            : null;

        private int UsageError()
        {
            errors.WriteLine(Usage);
            return ExitUsage;
        }
    }
}

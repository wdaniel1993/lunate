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
    bool? StdinIsTerminal = null
);

/// <summary>
/// The <c>lunate auth</c> command: list, login, logout and status without the TUI and without a
/// network call. Every path reports names and sources, never a credential value; a key read from a
/// pipe is the same secret as one typed at the masked prompt, so neither is echoed. Exit codes: 0
/// success, 1 the command failed or nothing is configured, 2 usage.
/// </summary>
internal static class AuthCommand
{
    internal const int ExitOk = 0;
    internal const int ExitFailure = 1;
    internal const int ExitUsage = 2;

    internal const string Usage = "Usage: lunate auth <list|login|logout|status>";

    internal const string LoginUsage = "Usage: lunate auth login <provider> [--key-stdin]";

    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter errors,
        AuthCommandSeams? seams = null
    ) => new Runner(output, errors, seams ?? new AuthCommandSeams()).Run(args);

    private sealed class Runner(TextWriter output, TextWriter errors, AuthCommandSeams seams)
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
            var rows = new List<(string Name, string Source)>(ProviderPresets.All.Count);
            var presetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProviderPreset preset in ProviderPresets.All)
            {
                presetIds.Add(preset.Id);
                string? variable = Variable(preset, Environment);
                string source =
                    variable is not null ? $"environment ({variable})"
                    : auth.Keys.ContainsKey(preset.Id) ? "stored"
                    : "not set";
                rows.Add((preset.Id, source));
            }

            foreach (
                string name in auth
                    .Keys.Keys.Where(name => !presetIds.Contains(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
            )
            {
                rows.Add((name, "stored"));
            }

            int width = rows.Max(row => row.Name.Length);
            foreach ((string name, string source) in rows)
            {
                output.WriteLine($"{name.PadRight(width)}  {source}");
            }

            return ExitOk;
        }

        private int Login(string[] args)
        {
            bool keyStdin = false;
            string? providerToken = null;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--key-stdin", StringComparison.Ordinal) && !keyStdin)
                {
                    keyStdin = true;
                    continue;
                }

                if (arg.StartsWith('-') || providerToken is not null)
                {
                    return UsageError();
                }

                providerToken = arg;
            }

            if (providerToken is null)
            {
                bool terminal = IsTerminal;
                TextWriter target = terminal ? output : errors;
                foreach (ProviderPreset entry in ProviderPresets.All)
                {
                    target.WriteLine($"{entry.Id}  {entry.Name}");
                }

                target.WriteLine(LoginUsage);
                return terminal ? ExitOk : ExitUsage;
            }

            // The token may be a pasted key, so an unknown provider error never echoes it.
            if (ProviderPresets.Find(providerToken) is not { } preset)
            {
                errors.WriteLine($"lunate: unknown provider; providers: {Supported}");
                return ExitUsage;
            }

            AuthStore auth = AuthStore.Load(seams.AuthPath, Environment);
            string? secret;
            if (keyStdin)
            {
                secret = (seams.Input ?? Console.In).ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(secret))
                {
                    errors.WriteLine(
                        "lunate: --key-stdin read no key; pipe the key on a single line, then run it again"
                    );
                    return ExitUsage;
                }
            }
            else if (!IsTerminal)
            {
                errors.WriteLine(
                    "lunate: stdin is not a terminal; pipe the key and pass --key-stdin"
                );
                return ExitUsage;
            }
            else
            {
                ISecretPrompt prompt = seams.SecretPrompt ?? new ConsoleSecretPrompt(errors);
                secret = prompt
                    .ReadAsync($"API key for {preset.Id}", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                if (string.IsNullOrWhiteSpace(secret))
                {
                    errors.WriteLine("auth: login cancelled");
                    return ExitFailure;
                }
            }

            string? previous = auth.Keys.GetValueOrDefault(preset.Id);
            auth.Set(preset.Id, secret);
            try
            {
                auth.Save();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                if (previous is null)
                {
                    auth.Remove(preset.Id);
                }
                else
                {
                    auth.Set(preset.Id, previous);
                }

                errors.WriteLine(
                    $"lunate: could not save the key for '{preset.Id}': {exception.Message}"
                );
                return ExitFailure;
            }

            output.WriteLine($"auth: stored the key for '{preset.Id}'");
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
                ModelCatalog.Load(seams.ModelsPath)
            );
            output.WriteLine($"default provider: {model.Provider}");

            AuthStore auth = AuthStore.Load(seams.AuthPath, Environment);
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

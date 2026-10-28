using Lunate.Ai;

namespace Lunate.Coding;

internal static partial class AuthCommand
{
    private sealed partial class Runner
    {
        private int Login(string[] args)
        {
            bool keyStdin = false;
            bool custom = false;
            string? providerToken = null;
            string? id = null;
            string? baseUrl = null;
            string? model = null;
            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                switch (arg)
                {
                    case "--key-stdin":
                        if (keyStdin)
                        {
                            return UsageError();
                        }

                        keyStdin = true;
                        continue;
                    case "--custom":
                        if (custom)
                        {
                            return UsageError();
                        }

                        custom = true;
                        continue;
                    case "--id" or "--base-url" or "--model":
                        if (index + 1 >= args.Length)
                        {
                            return UsageError();
                        }

                        string value = args[++index];
                        ref string? target = ref id;
                        if (arg == "--base-url")
                        {
                            target = ref baseUrl;
                        }
                        else if (arg == "--model")
                        {
                            target = ref model;
                        }

                        if (target is not null)
                        {
                            return UsageError();
                        }

                        target = value;
                        continue;
                }

                if (arg.StartsWith('-') || providerToken is not null)
                {
                    return UsageError();
                }

                providerToken = arg;
            }

            if (providerToken is not null && custom)
            {
                return UsageError();
            }

            if (custom)
            {
                return LoginCustom(id, baseUrl, model, keyStdin);
            }

            if (id is not null || baseUrl is not null || model is not null)
            {
                return UsageError();
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

        /// <summary>
        /// The custom OpenAI-compatible save: collect the id, base URL and model id (from flags or
        /// visible prompts, in that order), validate them, then read the key and write it to the
        /// auth store before the settings entry. A refused field writes nothing.
        /// </summary>
        private int LoginCustom(string? id, string? baseUrl, string? model, bool keyStdin)
        {
            if (keyStdin && (id is null || baseUrl is null || model is null))
            {
                errors.WriteLine(
                    "lunate: --custom with --key-stdin needs --id, --base-url and --model"
                );
                errors.WriteLine(LoginUsage);
                return ExitUsage;
            }

            if (!keyStdin && !IsTerminal)
            {
                errors.WriteLine(
                    "lunate: stdin is not a terminal; pipe the key and pass --key-stdin"
                );
                return ExitUsage;
            }

            TextReader input = seams.Input ?? Console.In;
            id ??= ReadField(input, "Provider id");
            baseUrl ??= ReadField(input, "Base URL");
            model ??= ReadField(input, "Model id");

            if (
                !CustomProvider.TryCreate(
                    id,
                    baseUrl,
                    model,
                    catalogName: null,
                    out CustomProvider? provider,
                    out string? error
                ) || provider is null
            )
            {
                errors.WriteLine($"lunate: {error}");
                return ExitUsage;
            }

            string? secret;
            if (keyStdin)
            {
                secret = input.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(secret))
                {
                    errors.WriteLine(
                        "lunate: --key-stdin read no key; pipe the key on a single line, then run it again"
                    );
                    return ExitUsage;
                }
            }
            else
            {
                ISecretPrompt prompt = seams.SecretPrompt ?? new ConsoleSecretPrompt(errors);
                secret = prompt
                    .ReadAsync($"API key for {provider.Id}", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                if (string.IsNullOrWhiteSpace(secret))
                {
                    errors.WriteLine("auth: login cancelled");
                    return ExitFailure;
                }
            }

            AuthStore auth = AuthStore.Load(seams.AuthPath, Environment);
            string? previous = auth.Keys.GetValueOrDefault(provider.Id);
            auth.Set(provider.Id, secret);
            try
            {
                auth.Save();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                RestoreKey(auth, provider.Id, previous);
                errors.WriteLine(
                    $"lunate: could not save the key for '{provider.Id}': {exception.Message}"
                );
                return ExitFailure;
            }

            try
            {
                SettingsStore.WriteProvider(provider, seams.SettingsPath);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                )
            {
                // The settings entry is the second write: a failure restores the key state so a
                // provider is never half-saved.
                RestoreKey(auth, provider.Id, previous);
                try
                {
                    auth.Save();
                }
                catch (Exception restore)
                    when (restore is IOException or UnauthorizedAccessException)
                {
                    // The original failure is the one worth reporting.
                }

                errors.WriteLine(
                    $"lunate: could not save the custom provider '{provider.Id}': {exception.Message}"
                );
                return ExitFailure;
            }

            output.WriteLine(
                $"auth: stored the custom provider '{provider.Id}'; its model id is '{provider.CatalogName}'"
            );
            return ExitOk;
        }

        private string? ReadField(TextReader input, string label)
        {
            output.Write($"{label}: ");
            output.Flush();
            return input.ReadLine()?.Trim();
        }

        private static void RestoreKey(AuthStore auth, string id, string? previous)
        {
            if (previous is null)
            {
                auth.Remove(id);
            }
            else
            {
                auth.Set(id, previous);
            }
        }
    }
}

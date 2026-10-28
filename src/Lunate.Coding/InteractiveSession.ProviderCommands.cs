using Lunate.Ai;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The in-flight (or last) login save; tests await it to observe the completion.</summary>
    internal Task? LoginTask { get; private set; }

    /// <summary>
    /// <c>/login &lt;provider&gt;</c>: validates the preset id, reads the key through the masked
    /// secret prompt and saves it under the preset id. Empty input cancels; no network call
    /// validates the key, and no notice, frame or session line ever carries the typed value.
    /// </summary>
    private async Task LoginAsync(string argument, CancellationToken ct, string command = "/login")
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        // Only the provider token is ever interpreted, reported or resolved: a typed key after it
        // must never reach a notice, the history file or an error message.
        int separator = argument.IndexOfAny([' ', '\t', '\r', '\n']);
        string provider = separator < 0 ? argument : argument[..separator];
        // A single token that is not a preset may itself be a pasted key, so the login error
        // never echoes it; /logout keeps naming the provider it could not find.
        if (ResolvePreset(command, provider, echoArgument: false) is not { } preset)
        {
            return;
        }

        if (separator >= 0)
        {
            Notify(
                command + " takes no key argument; the key is read from the masked prompt",
                NoticeLevel.Warning
            );
            return;
        }

        await BeginPresetLogin(preset, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the masked prompt for one preset and keeps the save observable through
    /// <see cref="LoginTask"/>. A prompt that completes synchronously is awaited here so a scripted
    /// or already-cancelled read stays on the caller's thread; the live TUI prompt is driven by the
    /// key pump this command already runs on, so awaiting it before its first key would deadlock and
    /// the continuation saves and notifies on the thread pool instead.
    /// </summary>
    private async Task BeginPresetLogin(ProviderPreset preset, CancellationToken ct)
    {
        Task<string?> pending = _secretPrompt.ReadAsync($"API key for {preset.Id}", ct);
        LoginTask = FinishLoginAsync(preset, pending, ct);
        if (pending.IsCompleted)
        {
            await LoginTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Saves the read key once the prompt completes: Enter stores it under the preset id, a null
    /// or blank value is a cancelled login, and a failed save rolls back and reports without the
    /// value. A cancelled read ends quietly because the session is quitting.
    /// </summary>
    private async Task FinishLoginAsync(
        ProviderPreset preset,
        Task<string?> pending,
        CancellationToken ct
    )
    {
        try
        {
            string? secret = await pending.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(secret))
            {
                Notify("login cancelled");
                return;
            }

            string? previous = _auth.Keys.GetValueOrDefault(preset.Id);
            _auth.Set(preset.Id, secret);
            try
            {
                _auth.Save();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                if (previous is null)
                {
                    _auth.Remove(preset.Id);
                }
                else
                {
                    _auth.Set(preset.Id, previous);
                }

                Notify(
                    $"could not save the key for '{preset.Id}': {exception.Message}",
                    NoticeLevel.Error
                );
                return;
            }

            Notify("logged in: " + preset.Id + " — /model to choose a model");
            OfferVerification(preset.Id, VerificationModel(preset.Id));
        }
        catch (OperationCanceledException)
        {
            // A cancelled read or a shutdown notify: the session is quitting, so stay quiet.
        }
        catch (Exception exception)
        {
            // A faulted prompt or any other unexpected failure: report the failure without the
            // typed value, and never let the task fault unobserved.
            Notify(
                $"login failed: {exception.GetType().Name}: {exception.Message}",
                NoticeLevel.Error
            );
        }
    }

    /// <summary>
    /// <c>/logout &lt;provider&gt;</c>: removes the stored preset key and saves. The notice names a
    /// still-set environment variable but never its value.
    /// </summary>
    private void Logout(string argument)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        if (ResolvePreset("/logout", argument) is not { } preset)
        {
            return;
        }

        string? previous = _auth.Keys.GetValueOrDefault(preset.Id);
        if (previous is null)
        {
            Notify($"no stored key for '{preset.Id}'{EnvironmentSuffix(preset)}");
            return;
        }

        _auth.Remove(preset.Id);
        try
        {
            _auth.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _auth.Set(preset.Id, previous);
            Notify($"could not save the auth file: {exception.Message}", NoticeLevel.Error);
            return;
        }

        Notify($"removed the stored key for '{preset.Id}'{EnvironmentSuffix(preset)}");
    }

    private ProviderPreset? ResolvePreset(string command, string argument, bool echoArgument = true)
    {
        if (ProviderPresets.Find(argument) is { } preset)
        {
            return preset;
        }

        string supported = string.Join(", ", ProviderPresets.All.Select(preset => preset.Id));
        string usage = FormattableString.Invariant($"{command} <provider>; providers: {supported}");
        Notify(
            argument.Length == 0 ? "usage: " + usage
                : echoArgument ? $"unknown provider '{argument}'; usage: {usage}"
                : $"unknown provider for {command}; usage: {usage}",
            NoticeLevel.Warning
        );
        return null;
    }

    private string EnvironmentSuffix(ProviderPreset preset)
    {
        string? variable =
            !string.IsNullOrEmpty(_environment(preset.KeyVariable)) ? preset.KeyVariable
            : preset.KeyVariableAlias is { } alias && !string.IsNullOrEmpty(_environment(alias))
                ? alias
            : null;
        return variable is null
            ? string.Empty
            : FormattableString.Invariant($"; {variable} is still set in the environment");
    }
}

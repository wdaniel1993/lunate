using Lunate.Ai;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// <c>/login &lt;provider&gt;</c>: validates the preset id, reads the key through the masked
    /// secret prompt and saves it under the preset id. Empty input cancels; no network call
    /// validates the key, and no notice, frame or session line ever carries the typed value.
    /// </summary>
    private async Task LoginAsync(string argument, CancellationToken ct)
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        if (ResolvePreset("/login", argument) is not { } preset)
        {
            return;
        }

        if (_options.SecretPrompt is not { } prompt)
        {
            Notify("login needs an interactive secret prompt", NoticeLevel.Warning);
            return;
        }

        string? secret = await prompt
            .ReadAsync($"API key for {preset.Id}", ct)
            .ConfigureAwait(false);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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

        Notify("logged in: " + preset.Id);
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

    private ProviderPreset? ResolvePreset(string command, string argument)
    {
        if (ProviderPresets.Find(argument) is { } preset)
        {
            return preset;
        }

        string supported = string.Join(", ", ProviderPresets.All.Select(preset => preset.Id));
        string usage = FormattableString.Invariant($"{command} <provider>; providers: {supported}");
        Notify(
            argument.Length == 0
                ? "usage: " + usage
                : $"unknown provider '{argument}'; usage: {usage}",
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

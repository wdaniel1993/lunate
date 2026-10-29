using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// The custom row's picker value. The leading NUL can never be a provider id from a preset or
    /// the settings file, so the sentinel cannot collide with a real selection.
    /// </summary>
    internal const string AuthCustomValue = "\0custom";

    /// <summary>The custom row's exact label, as the spec and the CLI list it.</summary>
    internal const string AuthCustomLabel = "Custom OpenAI-compatible endpoint…";

    private string _verifyProviderId = string.Empty;
    private ModelInfo? _verifyModel;

    /// <summary>The in-flight (or last) verification; tests await it to observe the result.</summary>
    internal Task? VerifyTask { get; private set; }

    /// <summary>
    /// Whether the first-run offer applies: a terminal session whose model has no usable
    /// credential. It fires once, after composition, so picker keys are accepted; Esc dismisses it
    /// and nothing reopens it.
    /// </summary>
    private bool OffersFirstRunAuth() => HasMissingCredential(_model);

    /// <summary>
    /// <c>/auth</c>: the live-area picker over every preset in file order followed by the custom
    /// endpoint row. Esc cancels without a write; selecting a preset starts the same masked prompt
    /// and save path as <c>/login</c>.
    /// </summary>
    private void OpenAuthPicker()
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        List<string> labels = [];
        List<string> values = [];
        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            labels.Add($"{preset.Id} — {preset.Name}");
            values.Add(preset.Id);
        }

        labels.Add(AuthCustomLabel);
        values.Add(AuthCustomValue);
        OpenPicker(PickerKind.AuthProvider, "Select provider", labels, values, 0);
    }

    /// <summary>
    /// Starts the four-field custom flow without awaiting the prompt: the live prompt is driven by
    /// the key pump, so its continuation runs off-pump when the real prompt is pending. A scripted
    /// or already-completed prompt runs synchronously, so tests observe the write when the call
    /// returns.
    /// </summary>
    private void BeginCustomLogin(CancellationToken ct) => LoginTask = CustomLoginAsync(ct);

    /// <summary>
    /// Collects id, base URL, model id and key through the existing masked prompt, validates the
    /// endpoint before the key is ever read, then writes the key to the auth store and the provider
    /// to the settings file. A failed second write restores the key, so a provider is never
    /// half-saved. The notice names the provider and points at <c>/model</c>, never the key.
    /// </summary>
    private async Task CustomLoginAsync(CancellationToken ct)
    {
        try
        {
            string? id = await _secretPrompt
                .ReadAsync("Custom provider id", ct)
                .ConfigureAwait(false);
            if (id is null)
            {
                Notify("login cancelled");
                return;
            }

            string? baseUrl = await _secretPrompt
                .ReadAsync("Base URL for the custom provider", ct)
                .ConfigureAwait(false);
            if (baseUrl is null)
            {
                Notify("login cancelled");
                return;
            }

            string? model = await _secretPrompt
                .ReadAsync("Model id for the custom provider", ct)
                .ConfigureAwait(false);
            if (model is null)
            {
                Notify("login cancelled");
                return;
            }

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
                Notify($"custom provider refused: {error}", NoticeLevel.Warning);
                return;
            }

            string? secret = await _secretPrompt
                .ReadAsync($"API key for {provider.Id}", ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(secret))
            {
                Notify("login cancelled");
                return;
            }

            string? previous = _auth.Keys.GetValueOrDefault(provider.Id);
            _auth.Set(provider.Id, secret);
            try
            {
                _auth.Save();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                RestoreAuthKey(provider.Id, previous);
                Notify(
                    $"could not save the key for '{provider.Id}': {exception.Message}",
                    NoticeLevel.Error
                );
                return;
            }

            try
            {
                SettingsStore.WriteProvider(provider, _options.SettingsPath);
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
                RestoreAuthKey(provider.Id, previous);
                try
                {
                    _auth.Save();
                }
                catch (Exception restore)
                    when (restore is IOException or UnauthorizedAccessException)
                {
                    // The original failure is the one worth reporting.
                }

                Notify(
                    $"could not save the custom provider '{provider.Id}': {exception.Message}",
                    NoticeLevel.Error
                );
                return;
            }

            // The catalog is read again so the notice's /model promise is true for this session.
            _settings = SettingsStore.Resolve(_options.SettingsPath, _options.Environment);
            _settings = SettingsStore.ApplyLaunch(_settings, _options.Launch);
            _catalog = HarnessFactory.LoadCatalog(_settings, _options.ModelsPath);

            Notify($"logged in: {provider.Id} — /model to choose {provider.CatalogName}");
            OfferVerification(provider.Id, provider.ToModelInfo());
        }
        catch (OperationCanceledException)
        {
            // A cancelled read or a shutdown notify: the session is quitting, so stay quiet.
        }
        catch (Exception exception)
        {
            Notify(
                $"login failed: {exception.GetType().Name}: {exception.Message}",
                NoticeLevel.Error
            );
        }
    }

    private void RestoreAuthKey(string id, string? previous)
    {
        if (previous is null)
        {
            _auth.Remove(id);
        }
        else
        {
            _auth.Set(id, previous);
        }
    }

    /// <summary>
    /// The model a verification request uses: the active model when it belongs to the provider,
    /// otherwise the first catalog entry for it, and the custom provider's own catalog entry.
    /// </summary>
    private ModelInfo? VerificationModel(string providerId, CustomProvider? custom = null)
    {
        if (custom is not null)
        {
            return custom.ToModelInfo();
        }

        if (string.Equals(_model.Provider, providerId, StringComparison.Ordinal))
        {
            return _model;
        }

        return _catalog.Models.FirstOrDefault(model =>
            string.Equals(model.Provider, providerId, StringComparison.Ordinal)
        );
    }

    /// <summary>
    /// Asks before spending a request. The safe row (skip) is selected, so Enter and Esc both
    /// leave the saved key and the network untouched; a preset with no catalog model offers no
    /// verification at all.
    /// </summary>
    private void OfferVerification(string providerId, ModelInfo? model)
    {
        if (model is null)
        {
            return;
        }

        _verifyProviderId = providerId;
        _verifyModel = model;
        OpenPicker(
            PickerKind.AuthVerify,
            $"Verify {providerId} with one request?",
            ["Yes, send one request", "Skip verification"],
            ["yes", "no"],
            1
        );
    }

    /// <summary>Runs the accepted verification; the task is exposed so tests can await the result.</summary>
    private void StartVerification(CancellationToken ct)
    {
        if (_verifyModel is not { } model)
        {
            return;
        }

        VerifyTask = VerifyAsync(_verifyProviderId, model, ct);
    }

    /// <summary>
    /// One non-streaming request with one output token. Success is reported by name; a failure
    /// reports the reason with the saved key redacted and leaves the store exactly as it was.
    /// </summary>
    private async Task VerifyAsync(string providerId, ModelInfo model, CancellationToken ct)
    {
        try
        {
            IChatClient client = _factory.Create(model);
            await client
                .GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "Reply with OK.")],
                    new ChatOptions { MaxOutputTokens = 1 },
                    ct
                )
                .ConfigureAwait(false);
            Notify(FormattableString.Invariant($"verified {providerId} with one request"));
        }
        catch (OperationCanceledException)
        {
            // The session is quitting: the verification's own report is not news any more.
        }
        catch (Exception exception)
        {
            string reason = exception.Message;
            if (_auth.TryGet(providerId) is { Length: > 0 } secret)
            {
                reason = reason.Replace(secret, "[redacted]", StringComparison.Ordinal);
            }

            Notify(
                FormattableString.Invariant($"verification failed for {providerId}: {reason}"),
                NoticeLevel.Error
            );
        }
    }
}

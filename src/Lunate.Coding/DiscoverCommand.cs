using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>The outcome of a discovery run: the draft on success, an actionable message otherwise.</summary>
public sealed record DiscoverResult(bool Succeeded, string Draft, string? Error);

/// <summary>
/// The <c>--discover</c> helper: lists an OpenAI-compatible endpoint's models
/// (<c>GET {base}/models</c>, response shape <c>{ "data": [ { "id": ... } ] }</c>) and returns a
/// <c>models.json</c>-shaped draft for the user catalog. Nothing is ever written to disk; the
/// transport, the key lookup and the environment lookup are injectable, so tests are fully offline.
/// </summary>
public static class DiscoverCommand
{
    private const string OpenAiProvider = "openai";
    private const string AnthropicProvider = "anthropic";
    private const string OpenAiApiKeyVariable = "OPENAI_API_KEY";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions DraftJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Runs discovery for a catalog name or an http(s) URL.</summary>
    public static async Task<DiscoverResult> RunAsync(
        string target,
        ModelCatalog catalog,
        HttpMessageHandler? handler = null,
        Func<string, string?>? namedKeySource = null,
        Func<string, string?>? environment = null,
        CancellationToken ct = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(catalog);

        string? provider = null;
        string? authRef = null;
        bool customEndpoint = false;
        bool qualified = false;
        string baseUrl;

        if (IsHttpUrl(target, out string url))
        {
            baseUrl = TrimTrailingSlash(url);
            customEndpoint = true;
        }
        else if (ProviderPresets.Find(target) is { } presetTarget)
        {
            // A preset name is a provider target even when no catalog entry carries that id.
            provider = presetTarget.Id;
            qualified = presetTarget.Adapter == ProviderAdapter.OpenAiCompatible;
            baseUrl = TrimTrailingSlash(presetTarget.BaseUrl.AbsoluteUri);
        }
        else
        {
            ModelInfo? model = catalog.Find(target);
            if (model is null)
            {
                return Failure(
                    $"Unknown model or preset '{target}'. Add it to ~/.lunate/models.json, name a preset, or pass an http(s) URL."
                );
            }

            provider = model.Provider;
            authRef = model.AuthRef;
            if (model.Endpoint is not null)
            {
                baseUrl = TrimTrailingSlash(model.Endpoint.AbsoluteUri);
                customEndpoint = true;
            }
            else if (ProviderPresets.Find(model.Provider) is { } modelPreset)
            {
                // Native presets keep bare ids; the OpenAI-compatible ones emit qualified ids.
                qualified = modelPreset.Adapter == ProviderAdapter.OpenAiCompatible;
                baseUrl = TrimTrailingSlash(modelPreset.BaseUrl.AbsoluteUri);
            }
            else
            {
                return Failure(
                    $"Model '{target}' has no endpoint and provider '{model.Provider}' has no known default endpoint."
                );
            }
        }

        string? key;
        if (!string.IsNullOrEmpty(authRef))
        {
            key = namedKeySource?.Invoke(authRef);
            if (string.IsNullOrEmpty(key))
            {
                return Failure(
                    $"The auth reference '{authRef}' cannot be resolved. "
                        + $"Add a key named '{authRef}' to auth.json or set the matching environment variable."
                );
            }
        }
        else
        {
            key = PresetKey(provider, environment, namedKeySource);
        }

        string requestUrl = $"{baseUrl}/models";
        using var client = new HttpClient(
            handler ?? new HttpClientHandler(),
            disposeHandler: handler is null
        )
        {
            Timeout = RequestTimeout,
        };

        HttpResponseMessage response;
        try
        {
            using var request = CreateRequest(requestUrl, provider, key);
            response = await client.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or TaskCanceledException
                        or InvalidOperationException
            )
        {
            return Failure($"Discovering models from {requestUrl} failed: {exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    $"Discovering models from {requestUrl} failed: "
                        + $"HTTP {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)} ({response.ReasonPhrase})."
                );
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            return ParseListing(
                body,
                requestUrl,
                provider,
                customEndpoint,
                baseUrl,
                qualified,
                authRef
            );
        }
    }

    private static DiscoverResult ParseListing(
        string body,
        string requestUrl,
        string? provider,
        bool customEndpoint,
        string baseUrl,
        bool qualified,
        string? authRef
    )
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            return Failure(
                $"The response from {requestUrl} is not valid JSON ({exception.Message})."
            );
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array
            )
            {
                return Failure(
                    $"The response from {requestUrl} does not contain a 'data' array of models."
                );
            }

            var models = new List<DraftModel>();
            foreach (JsonElement entry in data.EnumerateArray())
            {
                if (
                    entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("id", out JsonElement id)
                    || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(id.GetString())
                )
                {
                    return Failure(
                        $"The response from {requestUrl} has a model entry without an id."
                    );
                }

                string listed = id.GetString()!;
                models.Add(
                    qualified
                        ? new DraftModel(
                            $"{provider}/{listed}",
                            provider,
                            Endpoint: null,
                            authRef,
                            WireId: listed
                        )
                        : new DraftModel(
                            listed,
                            provider,
                            customEndpoint ? baseUrl : null,
                            authRef,
                            WireId: null
                        )
                );
            }

            string draft = JsonSerializer.Serialize(
                new DraftFile(ModelCatalog.SupportedSchemaVersion, models),
                DraftJson
            );
            return new DiscoverResult(true, draft, null);
        }
    }

    private static HttpRequestMessage CreateRequest(string url, string? provider, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (string.IsNullOrEmpty(key))
        {
            return request;
        }

        if (provider == AnthropicProvider)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", key);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        }

        return request;
    }

    private static bool IsHttpUrl(string target, out string url)
    {
        if (
            Uri.TryCreate(target, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        )
        {
            url = parsed.AbsoluteUri;
            return true;
        }

        url = string.Empty;
        return false;
    }

    private static string TrimTrailingSlash(string url) => url.TrimEnd('/');

    /// <summary>
    /// The key slot discovery reads: the preset's variable (its alias second), then the auth store
    /// under the preset id. A user-declared provider without a preset keeps the OpenAI key slot it
    /// has always used.
    /// </summary>
    private static string? PresetKey(
        string? provider,
        Func<string, string?>? environment,
        Func<string, string?>? namedKeySource
    )
    {
        if (ProviderPresets.Find(provider ?? OpenAiProvider) is { } preset)
        {
            string? key = EnvironmentValue(preset.KeyVariable, environment);
            if (string.IsNullOrEmpty(key) && preset.KeyVariableAlias is { } alias)
            {
                key = EnvironmentValue(alias, environment);
            }

            return string.IsNullOrEmpty(key) ? namedKeySource?.Invoke(preset.Id) : key;
        }

        return EnvironmentValue(OpenAiApiKeyVariable, environment)
            ?? namedKeySource?.Invoke(provider ?? OpenAiProvider);
    }

    private static string? EnvironmentValue(string variable, Func<string, string?>? environment) =>
        environment is null ? Environment.GetEnvironmentVariable(variable) : environment(variable);

    private static DiscoverResult Failure(string message) => new(false, string.Empty, message);

    private sealed record DraftFile(int SchemaVersion, IReadOnlyList<DraftModel> Models);

    private sealed record DraftModel(
        string Id,
        string? Provider,
        string? Endpoint,
        string? Auth,
        string? WireId
    );
}

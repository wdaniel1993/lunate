using System.Net;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class ProviderReachabilityTests
{
    private const string ProviderSecret = "fixture-only-provider-secret";
    private const string HeaderSecret = "fixture-only-header-secret";

    [Fact]
    public void Auth_list_reports_presence_and_bounded_reachability_without_sending_secrets()
    {
        using var temp = new TempDirectory();
        string authPath = temp.File("auth.json");
        string providersPath = temp.File("providers.json");
        AuthStore auth = AuthStore.Load(authPath, _ => null);
        auth.Set("work-key", ProviderSecret);
        auth.Set("header-key", HeaderSecret);
        auth.Save();
        ProviderStore.Save(
            providersPath,
            [
                Entry(
                    "work-openai",
                    "https://work.example.test/v1",
                    "auth:work-key",
                    "auth:header-key"
                ),
                Entry(
                    "personal-openai",
                    "https://personal.example.test/v1",
                    "env:PERSONAL_OPENAI_KEY",
                    "auth:header-key"
                ),
                Entry(
                    "slow-openai",
                    "https://slow.example.test/v1",
                    "helper:corp-credential",
                    "auth:header-key"
                ),
            ]
        );
        var handler = new ScriptedHandler();

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = AuthCommand.Run(
            ["list"],
            output,
            errors,
            new AuthCommandSeams(
                AuthPath: authPath,
                SettingsPath: temp.File("settings.json"),
                Environment: _ => null,
                ProviderProbeHandler: handler,
                ProviderProbeTimeout: WaitBudgets.ProviderProbeTimeout
            )
        );
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("work-openai (openai)", text, StringComparison.Ordinal);
        Assert.Contains(
            "credential: present",
            LineFor(text, "work-openai"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "reachability: reachable",
            LineFor(text, "work-openai"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "credential: absent",
            LineFor(text, "personal-openai"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "reachability: unreachable",
            LineFor(text, "personal-openai"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "credential: present",
            LineFor(text, "slow-openai"),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "reachability: unknown",
            LineFor(text, "slow-openai"),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(ProviderSecret, text + errors, StringComparison.Ordinal);
        Assert.DoesNotContain(HeaderSecret, text + errors, StringComparison.Ordinal);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request =>
            {
                Assert.Null(request.Headers.Authorization);
                Assert.False(request.Headers.Contains("X-Optional"));
                Assert.Null(request.Content);
            }
        );
        Assert.Equal(
            ["personal-openai", "slow-openai", "work-openai"],
            handler
                .Requests.Select(request =>
                    request.RequestUri!.Host switch
                    {
                        "personal.example.test" => "personal-openai",
                        "slow.example.test" => "slow-openai",
                        "work.example.test" => "work-openai",
                        _ => "unexpected",
                    }
                )
                .Order(StringComparer.Ordinal)
        );
    }

    private static ProviderEntry Entry(
        string name,
        string endpoint,
        string credentialReference,
        string headerReference
    )
    {
        bool created = ProviderEntry.TryCreate(
            name,
            "openai",
            new Uri(endpoint),
            credentialReference,
            new Dictionary<string, string> { ["X-Optional"] = headerReference },
            region: null,
            out ProviderEntry? entry,
            out string? error
        );
        Assert.True(created, error);
        return Assert.IsType<ProviderEntry>(entry);
    }

    private static string LineFor(string text, string provider) =>
        Assert.Single(
            text.Split(Environment.NewLine),
            line => line.StartsWith(provider, StringComparison.Ordinal)
        );

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return request.RequestUri!.Host switch
            {
                "work.example.test" => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
                "personal.example.test" => throw new HttpRequestException("fixture unreachable"),
                "slow.example.test" => await WaitForTimeout(cancellationToken),
                _ => throw new InvalidOperationException("Unexpected fixture endpoint"),
            };
        }

        private static async Task<HttpResponseMessage> WaitForTimeout(
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

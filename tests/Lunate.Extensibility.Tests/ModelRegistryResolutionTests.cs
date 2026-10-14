using System.Runtime.CompilerServices;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Tests;

public sealed class ModelRegistryResolutionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolution_order_is_virtual_then_programmatic_then_declared_then_catalog()
    {
        var factory = new RecordingFactory();
        var registry = new ModelRegistry(ModelRegistryRegistrationTests.Catalog("cat"), factory);
        var programmatic = new RecordingChatClient("programmatic");
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("prog", ["p"], () => programmatic)
        );
        registry.RegisterDeclared(
            "e",
            [ModelRegistryRegistrationTests.Provider("decl", "d")],
            new TestSecrets { ["key"] = "secret" }
        );
        registry.RegisterVirtualModel("e", Virtual("auto", ["p"]));

        Assert.Same(programmatic, registry.Resolve("p"));
        Assert.Equal("d", ((RecordingChatClient)registry.Resolve("d")).Id);
        Assert.Equal("cat", ((RecordingChatClient)registry.Resolve("cat")).Id);
        ChatResponse routed = await registry
            .Resolve("ext/e/auto")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct);
        Assert.Equal("reply from programmatic", routed.Text);
    }

    [Fact]
    public void An_unknown_model_id_is_a_teaching_error_listing_known_ids()
    {
        var registry = new ModelRegistry(ModelRegistryRegistrationTests.Catalog("cat"));
        registry.RegisterDeclared(
            "e",
            [ModelRegistryRegistrationTests.Provider("decl", "d")],
            new TestSecrets { ["key"] = "secret" }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("nope")
        );

        Assert.Contains("'nope'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cat", exception.Message, StringComparison.Ordinal);
        Assert.Contains("d", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_virtual_model_routes_per_request_with_one_client()
    {
        var fast = new RecordingChatClient("fast");
        var deep = new RecordingChatClient("deep");
        var registry = new ModelRegistry();
        registry.RegisterProvider("e", new ExtensionModelProvider("fast-p", ["fast"], () => fast));
        registry.RegisterProvider("e", new ExtensionModelProvider("deep-p", ["deep"], () => deep));
        registry.RegisterVirtualModel(
            "e",
            Virtual(
                "auto",
                ["deep", "fast"],
                context => context.Messages[0].Text.Length > 10 ? "deep" : "fast"
            )
        );

        IChatClient client = registry.Resolve("ext/e/auto");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "short")], null, Ct);
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "a much longer request")],
            null,
            Ct
        );

        Assert.Single(fast.Requests);
        Assert.Single(deep.Requests);
    }

    [Fact]
    public async Task Middleware_composes_by_load_order_then_priority_then_registration()
    {
        var calls = new List<string>();
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "first",
            new ExtensionModelProvider("p", ["m"], () => new RecordingChatClient("b"))
        );
        registry.RegisterModelMiddleware("first", Tagging(calls, "first-p5", 5));
        registry.RegisterModelMiddleware("first", Tagging(calls, "first-p1", 1));
        registry.RegisterModelMiddleware("second", Tagging(calls, "second-p0", 0));

        await registry
            .Resolve("m")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct);

        Assert.Equal(["first-p1", "first-p5", "second-p0"], calls);
    }

    [Fact]
    public async Task Equal_priority_keeps_registration_order()
    {
        var calls = new List<string>();
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("p", ["m"], () => new RecordingChatClient("b"))
        );
        registry.RegisterModelMiddleware("e", Tagging(calls, "first", 0));
        registry.RegisterModelMiddleware("e", Tagging(calls, "second", 0));

        await registry
            .Resolve("m")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct);

        Assert.Equal(["first", "second"], calls);
    }

    [Fact]
    public async Task BeforeRouting_sees_virtual_calls_and_AroundBackend_only_backends()
    {
        var calls = new List<string>();
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("p", ["backend"], () => new RecordingChatClient("b"))
        );
        registry.RegisterVirtualModel("e", Virtual("auto", ["backend"]));
        registry.RegisterModelMiddleware(
            "e",
            Tagging(calls, "before", 0, ModelMiddlewareSlot.BeforeRouting)
        );
        registry.RegisterModelMiddleware(
            "e",
            Tagging(calls, "around", 0, ModelMiddlewareSlot.AroundBackend)
        );

        await registry
            .Resolve("ext/e/auto")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct);
        calls.Clear();
        await registry
            .Resolve("backend")
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct);

        Assert.Equal(["before", "around"], calls);
    }

    [Fact]
    public async Task A_throwing_middleware_fails_closed_naming_the_extension()
    {
        var provider = new RecordingChatClient("backend");
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "breaker",
            new ExtensionModelProvider("p", ["m"], () => provider)
        );
        registry.RegisterModelMiddleware(
            "breaker",
            new ExtensionModelMiddleware(
                ModelMiddlewareSlot.BeforeRouting,
                0,
                _ => throw new InvalidOperationException("middleware exploded")
            )
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                registry
                    .Resolve("m")
                    .GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct)
        );

        Assert.Contains("'breaker'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("middleware exploded", exception.Message, StringComparison.Ordinal);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Provider_exceptions_surface_unchanged_through_middleware()
    {
        var provider = new RecordingChatClient("backend")
        {
            Failure = new HttpRequestException("boom"),
        };
        var registry = new ModelRegistry();
        registry.RegisterProvider("e", new ExtensionModelProvider("p", ["m"], () => provider));
        registry.RegisterModelMiddleware("e", Tagging([], "pass", 0));
        registry.RegisterModelMiddleware("e", Tagging([], "pass-through", 1));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            registry.Resolve("m").GetResponseAsync([new ChatMessage(ChatRole.User, "x")], null, Ct)
        );

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public void A_cycle_between_virtual_models_is_a_teaching_error_naming_the_chain()
    {
        var registry = new ModelRegistry();
        registry.RegisterVirtualModel("e", Virtual("a", ["ext/e/b"]));
        registry.RegisterVirtualModel("e", Virtual("b", ["ext/e/a"]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("ext/e/a")
        );

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ext/e/a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ext/e/b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_deeper_than_eight_is_a_teaching_error_naming_the_chain()
    {
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("p", ["end"], () => new RecordingChatClient("b"))
        );
        string previous = "end";
        for (var index = 0; index < 9; index++)
        {
            // Two extensions so the per-extension virtual-model cap does not bound the chain.
            string extensionId = index == 8 ? "last" : "e";
            string id = $"v{index}";
            registry.RegisterVirtualModel(extensionId, Virtual(id, [previous]));
            previous = $"ext/{extensionId}/{id}";
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("ext/last/v8")
        );

        Assert.Contains("deeper than 8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ext/last/v8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_backend_is_a_teaching_error_naming_the_virtual_model_and_backend()
    {
        var registry = new ModelRegistry();
        registry.RegisterVirtualModel("e", Virtual("auto", ["missing"]));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("ext/e/auto")
        );

        Assert.Contains("'ext/e/auto'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Virtual_capabilities_derive_from_the_weakest_backend()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared(
            "e",
            [Declared("small", 8192, true), Declared("big", 32768, false)],
            new TestSecrets { ["key"] = "s" }
        );
        registry.RegisterVirtualModel("e", Virtual("auto", ["small", "big"]));

        ModelCapabilities capabilities = registry.FindCapabilities("ext/e/auto")!;

        Assert.Equal(8192, capabilities.ContextWindow);
        Assert.False(capabilities.SupportsTools);
    }

    [Fact]
    public void A_virtual_capability_declaration_may_narrow_but_not_widen()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared(
            "e",
            [Declared("small", 8192, true)],
            new TestSecrets { ["key"] = "s" }
        );
        registry.RegisterVirtualModel(
            "e",
            Virtual("narrow", ["small"]) with
            {
                ContextWindow = 4096,
                SupportsTools = false,
            }
        );
        ModelCapabilities narrowed = registry.FindCapabilities("ext/e/narrow")!;
        Assert.Equal(4096, narrowed.ContextWindow);
        Assert.False(narrowed.SupportsTools);

        registry.RegisterVirtualModel(
            "e",
            Virtual("wide", ["small"]) with
            {
                ContextWindow = 32768,
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.FindCapabilities("ext/e/wide")
        );

        Assert.Contains("'ext/e/wide'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("32768", exception.Message, StringComparison.Ordinal);
        Assert.Contains("8192", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Declaring_tools_on_a_virtual_model_needs_every_backend_to_support_them()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared(
            "e",
            [Declared("no-tools", 8192, false)],
            new TestSecrets { ["key"] = "s" }
        );
        registry.RegisterVirtualModel(
            "e",
            Virtual("auto", ["no-tools"]) with
            {
                SupportsTools = true,
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.FindCapabilities("ext/e/auto")
        );

        Assert.Contains("supportsTools", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'no-tools'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_secret_is_a_teaching_error_naming_provider_and_secret()
    {
        var registry = new ModelRegistry(clientFactory: new RecordingFactory());
        registry.RegisterDeclared("e", [Declared("local", 8192, true)], new TestSecrets());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("local")
        );

        Assert.Contains("'ext/e/local'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'key'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_provider_instantiates_on_first_use_with_the_host_read_secret()
    {
        var factory = new RecordingFactory();
        var registry = new ModelRegistry(clientFactory: factory);
        registry.RegisterDeclared(
            "e",
            [Declared("local", 8192, true)],
            new TestSecrets { ["key"] = "secret-value" }
        );

        IChatClient first = registry.Resolve("local");
        IChatClient second = registry.Resolve("local");

        Assert.Same(first, second);
        Assert.Equal("secret-value", factory.ApiKeys.Single());
        Assert.Equal("local", factory.Models.Single().Id);
        Assert.Equal("http://localhost:11434/v1", factory.Models.Single().Endpoint!.AbsoluteUri);
        Assert.Equal(8192, factory.Models.Single().ContextWindow);
    }

    [Fact]
    public async Task A_tools_unsupported_declaration_refuses_requests_offering_tools()
    {
        var registry = new ModelRegistry(clientFactory: new RecordingFactory());
        registry.RegisterDeclared(
            "e",
            [Declared("local", 8192, false)],
            new TestSecrets { ["key"] = "secret-value" }
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                registry
                    .Resolve("local")
                    .GetResponseAsync(
                        [new ChatMessage(ChatRole.User, "x")],
                        new ChatOptions { Tools = [new StubTool()] },
                        Ct
                    )
        );

        Assert.Contains("'ext/e/local'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("supportsTools", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_throwing_provider_factory_is_a_teaching_error_naming_the_extension()
    {
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider(
                "p",
                ["m"],
                () => throw new InvalidOperationException("no socket")
            )
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("m")
        );

        Assert.Contains("'ext/e/p'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'e'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no socket", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_throwing_selector_is_a_teaching_error_naming_the_virtual_model()
    {
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("p", ["m"], () => new RecordingChatClient("b"))
        );
        registry.RegisterVirtualModel(
            "e",
            Virtual("auto", ["m"], _ => throw new InvalidOperationException("bad selector"))
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                registry.Resolve("ext/e/auto").GetResponseAsync([], null, Ct)
        );

        Assert.Contains("'ext/e/auto'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'e'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("bad selector", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_selector_returning_an_undeclared_backend_is_refused()
    {
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("p", ["m"], () => new RecordingChatClient("b"))
        );
        registry.RegisterVirtualModel("e", Virtual("auto", ["m"], _ => "other"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                registry.Resolve("ext/e/auto").GetResponseAsync([], null, Ct)
        );

        Assert.Contains("'other'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'ext/e/auto'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Programmatic_provider_capabilities_are_unknown_and_do_not_constrain()
    {
        var registry = new ModelRegistry();
        registry.RegisterProvider(
            "e",
            new ExtensionModelProvider("p", ["m"], () => new RecordingChatClient("b"))
        );
        registry.RegisterVirtualModel("e", Virtual("auto", ["m"]));

        ModelCapabilities provider = registry.FindCapabilities("m")!;
        Assert.Equal(0, provider.ContextWindow);
        Assert.True(provider.SupportsTools);

        ModelCapabilities derived = registry.FindCapabilities("ext/e/auto")!;
        Assert.Equal(0, derived.ContextWindow);
        Assert.True(derived.SupportsTools);
    }

    [Fact]
    public void An_empty_registry_still_resolves_the_catalog()
    {
        var factory = new RecordingFactory();
        var registry = new ModelRegistry(ModelRegistryRegistrationTests.Catalog("cat"), factory);

        IChatClient client = registry.Resolve("cat");

        Assert.Equal("cat", ((RecordingChatClient)client).Id);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("ext/x/auto"));
    }

    private static ExtensionModelMiddleware Tagging(
        List<string> calls,
        string label,
        int priority,
        ModelMiddlewareSlot slot = ModelMiddlewareSlot.BeforeRouting
    ) => new(slot, priority, inner => new TaggingClient(inner, label, calls));

    private static VirtualModelRegistration Virtual(
        string id,
        string[] backends,
        Func<ModelRouteContext, string>? select = null
    ) => new(id, backends, select ?? (_ => backends[0]));

    private static ModelProviderDescriptor Declared(string id, int window, bool tools) =>
        new(id, id, "http://localhost:11434/v1", "key", [id], window, tools);

    private sealed class StubTool : AITool
    {
        public override string Name => "stub";
    }

    private sealed class TaggingClient(IChatClient inner, string label, List<string> calls)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            calls.Add(label);
            return await base.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class RecordingChatClient(string id) : IChatClient
{
    public string Id { get; } = id;

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public Exception? Failure { get; init; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        lock (Requests)
        {
            Requests.Add([.. messages]);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, $"reply from {Id}"))
            {
                ModelId = Id,
            }
        );
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        lock (Requests)
        {
            Requests.Add([.. messages]);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, $"stream from {Id}")
        {
            ModelId = Id,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(string) ? Id : null;

    public void Dispose() { }
}

internal sealed class RecordingFactory : IChatClientFactory
{
    public List<ModelInfo> Models { get; } = [];

    public List<string?> ApiKeys { get; } = [];

    public IChatClient Create(ModelInfo model) => Create(model, null);

    public IChatClient Create(ModelInfo model, string? apiKey)
    {
        Models.Add(model);
        ApiKeys.Add(apiKey);
        return new RecordingChatClient(model.Id);
    }
}

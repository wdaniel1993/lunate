using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Testing;

/// <summary>
/// The command-invocation conformance probes (protocol version 2): the listing rule the host
/// applies to the advertised capability, and the three round trips a command-serving child must
/// pass — an invocation whose reply carries the remainder, a cancel mid-invocation that answers
/// <c>cancelled</c>, and a failing handler that answers <c>failed</c> with its message. Each probe
/// reports its outcome as one <see cref="RemoteConformanceStep"/>; a broken frame is a failed
/// step, never an exception out of the run.
/// </summary>
internal static class RemoteCommandConformance
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The listing rule: a child that advertises the capability has its declared commands offered;
    /// one that does not keeps them as declarations (`declared, not provided`).
    /// </summary>
    public static RemoteConformanceStep Listing(InitializeResult answer) =>
        new(
            "command listing",
            true,
            answer.Commands
                ? "the child advertises the commands capability; its declared commands are offered"
                : "the child does not advertise the commands capability; its declared commands are reported as declared, not provided"
        );

    /// <summary>The probe command round trip: the reply carries the remainder it was sent.</summary>
    public static async Task<RemoteConformanceStep> InvokeAsync(
        RemoteWire wire,
        RemoteConformanceOptions options,
        CancellationToken cancellationToken
    )
    {
        try
        {
            CommandInvokeResult reply = await SendAsync(
                    wire,
                    options,
                    options.ProbeCommand,
                    options.ProbeCommandArguments,
                    cancellationToken
                )
                .ConfigureAwait(false);
            bool completed =
                reply.Disposition == CommandInvokeDisposition.Completed
                && reply.Output is not null
                && reply.Output.Contains(options.ProbeCommandArguments, StringComparison.Ordinal);
            return new RemoteConformanceStep(
                "command invoke",
                completed,
                completed
                    ? $"answered completed: {reply.Output}"
                    : $"answered {Disposition(reply)}: {Describe(reply)}"
            );
        }
        catch (Exception exception)
        {
            return new RemoteConformanceStep("command invoke", false, Explain(exception));
        }
    }

    /// <summary>
    /// A cancel mid-invocation: the runner names the in-flight request id in
    /// <c>$/cancelRequest</c> and the reply must be <c>cancelled</c>, not a failure.
    /// </summary>
    public static async Task<RemoteConformanceStep> CancelAsync(
        RemoteWire wire,
        RemoteConformanceOptions options,
        string command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            long id = 0;
            Task<JsonElement?> call = wire.CallWithIdAsync(
                RemoteProtocol.CommandInvokeMethod,
                framed =>
                {
                    id = framed;
                    return Encode(new CommandInvokeRequest(command, string.Empty, framed));
                },
                options.Timeout,
                CancellationToken.None
            );
            await wire.NotifyAsync(RemoteProtocol.CancelMethod, CancelId(id), cancellationToken)
                .ConfigureAwait(false);
            CommandInvokeResult reply = Decode(
                await call.WaitAsync(cancellationToken).ConfigureAwait(false)
            );
            bool cancelled = reply.Disposition == CommandInvokeDisposition.Cancelled;
            return new RemoteConformanceStep(
                "command cancel",
                cancelled,
                cancelled ? "the invocation answered cancelled" : $"answered {Describe(reply)}"
            );
        }
        catch (Exception exception)
        {
            return new RemoteConformanceStep("command cancel", false, Explain(exception));
        }
    }

    /// <summary>A handler that fails answers <c>failed</c> carrying its message.</summary>
    public static async Task<RemoteConformanceStep> FailureAsync(
        RemoteWire wire,
        RemoteConformanceOptions options,
        string command,
        CancellationToken cancellationToken
    )
    {
        try
        {
            CommandInvokeResult reply = await SendAsync(
                    wire,
                    options,
                    command,
                    string.Empty,
                    cancellationToken
                )
                .ConfigureAwait(false);
            bool failed =
                reply.Disposition == CommandInvokeDisposition.Failed
                && !string.IsNullOrWhiteSpace(reply.Error);
            return new RemoteConformanceStep(
                "command failure",
                failed,
                failed
                    ? $"answered failed: {reply.Error}"
                    : $"answered {Disposition(reply)}: {Describe(reply)}"
            );
        }
        catch (Exception exception)
        {
            return new RemoteConformanceStep("command failure", false, Explain(exception));
        }
    }

    private static async Task<CommandInvokeResult> SendAsync(
        RemoteWire wire,
        RemoteConformanceOptions options,
        string name,
        string argument,
        CancellationToken cancellationToken
    )
    {
        JsonElement? result = await wire.CallWithIdAsync(
                RemoteProtocol.CommandInvokeMethod,
                id => Encode(new CommandInvokeRequest(name, argument, id)),
                options.Timeout,
                cancellationToken
            )
            .ConfigureAwait(false);
        return Decode(result);
    }

    private static CommandInvokeResult Decode(JsonElement? result) =>
        result?.Deserialize<CommandInvokeResult>(JsonOptions)
        ?? throw new InvalidOperationException(
            $"the {RemoteProtocol.CommandInvokeMethod} reply was empty"
        );

    private static JsonElement Encode<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonOptions);

    private static JsonElement CancelId(long id) =>
        JsonSerializer.SerializeToElement(
            new Dictionary<string, long> { ["id"] = id },
            JsonOptions
        );

    private static string Disposition(CommandInvokeResult reply) =>
        reply.Disposition.ToString().ToLowerInvariant();

    private static string Describe(CommandInvokeResult reply) =>
        reply.Error ?? reply.Output ?? "nothing";

    private static string Explain(Exception exception) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{exception.GetType().Name}: {exception.Message}"
        );
}

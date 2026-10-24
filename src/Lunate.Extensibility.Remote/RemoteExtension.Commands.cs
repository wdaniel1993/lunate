using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

public sealed partial class RemoteExtension
{
    /// <summary>
    /// The manifest commands this extension offers, as the source registered through the
    /// <see cref="IExtensionContext.RegisterCommandSource">extension context</see> — registration
    /// only, nothing starts. Because the registry wraps every registration in the same invocation
    /// lease an in-process source takes, an unload cancels a live remote invocation, names it in one
    /// notice and drains within the hook-handler bound without this adapter re-implementing any of
    /// it.
    /// </summary>
    private sealed class RemoteCommandSource(RemoteExtension extension) : ICommandSource
    {
        public string SourceId => extension.Id;

        /// <summary>
        /// The declared commands, but only when the child's handshake advertised the
        /// <c>commands</c> capability: a command that cannot run is never offered and dead.
        /// </summary>
        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(extension.DescribeCommands());

        public ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(invocation);
            return extension.InvokeCommandAsync(
                invocation.Name.TrimStart('/'),
                invocation.Argument,
                cancellationToken
            );
        }
    }

    /// <summary>The manifest's declared commands as descriptors; empty without the capability.</summary>
    private IReadOnlyList<CommandDescriptor> DescribeCommands()
    {
        if (!_commandsAvailable)
        {
            return [];
        }

        return
        [
            .. _options.Manifest.Commands.Select(name => new CommandDescriptor(
                name.TrimStart('/'),
                $"Runs in extension '{Id}' over the wire",
                ArgumentHint: null,
                SourceId: Id,
                Kind: CommandKind.Extension
            )),
        ];
    }

    /// <summary>
    /// Host → child: invokes one declared command over <c>command/invoke</c> with the bare name,
    /// the argument remainder and the request id the frame carries. The reply maps field by field
    /// onto the in-process result: output or no notice, <c>cancelled</c> as a silent end, and a
    /// failure raised as an exception so the registry renders the in-process error notice naming
    /// <c>ext:&lt;id&gt;:&lt;name&gt;</c>.
    /// </summary>
    internal async ValueTask<CommandResult> InvokeCommandAsync(
        string name,
        string argument,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_commandsAvailable)
        {
            // Only reachable from a stale registry snapshot: the command is not listed without the
            // capability, and it is never sent to a child that did not advertise it.
            throw new RemoteExtensionException(
                $"extension '{Id}': it did not advertise the commands capability, so command ext:{Id}:{name} was not sent."
            );
        }

        RemoteWire wire = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        JsonElement? answer;
        try
        {
            answer = await wire.CallWithIdAsync(
                    RemoteProtocol.CommandInvokeMethod,
                    id =>
                        JsonSerializer.SerializeToElement(
                            new CommandInvokeRequest(name, argument, id),
                            WireJson.Options
                        ),
                    CallTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (RemoteChannelClosedException exception)
        {
            throw new RemoteExtensionException(
                $"extension '{Id}': the process exited while command ext:{Id}:{name} was in flight: {exception.Message}",
                exception
            );
        }
        catch (RemoteCallException exception)
            when (exception.Code == RemoteProtocol.CancelledErrorCode)
        {
            // A child that lets the cancellation escape answers the wire's cancel error; it ended
            // the invocation just as a `cancelled` reply does, so it is not a failure notice.
            return CommandResult.None;
        }

        if (answer is null)
        {
            throw new RemoteExtensionException(
                $"command ext:{Id}:{name} answered with no result; the extension sent an empty reply."
            );
        }

        CommandInvokeResult reply;
        try
        {
            reply =
                answer.Value.Deserialize<CommandInvokeResult>(WireJson.Options)
                ?? throw new JsonException("the reply was null");
        }
        catch (JsonException exception)
        {
            throw new RemoteExtensionException(
                $"command ext:{Id}:{name} failed: the extension's reply was malformed ({exception.Message}).",
                exception
            );
        }

        return ToCommandResult(name, reply);
    }

    /// <summary>
    /// The one place the wire reply becomes the in-process result. A failure is raised rather than
    /// returned so the command registry appends the recovery step, exactly as it does for an
    /// in-process handler's exception.
    /// </summary>
    private CommandResult ToCommandResult(string name, CommandInvokeResult reply)
    {
        switch (reply.Disposition)
        {
            case CommandInvokeDisposition.Cancelled:
                return CommandResult.None;
            case CommandInvokeDisposition.Failed:
                throw new RemoteExtensionException(
                    string.IsNullOrWhiteSpace(reply.Error)
                        ? RemoteCommandInvoke.FailureWithoutMessage
                        : reply.Error
                );
            default:
                return string.IsNullOrEmpty(reply.Output)
                    ? CommandResult.None
                    : CommandResult.Notice($"ext:{Id}:{name}: {reply.Output}");
        }
    }
}

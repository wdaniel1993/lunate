using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The source every caller actually invokes for an extension command (design D3): it enters a lease
/// on the owning extension around the handler call, so no caller can obtain an unleased extension
/// source. An unload cancels the handler's token through the lease; a result that arrives after
/// that cancellation is discarded, and a host call into the disposed context is contained and
/// logged with the command id instead of escaping as an error notice or a crash (sign-off Q2).
/// </summary>
internal sealed class LeasedCommandSource(
    string extensionId,
    ICommandSource inner,
    CommandInvocationLeases leases
) : ICommandSource
{
    internal string ExtensionId => extensionId;

    internal ICommandSource Inner => inner;

    public string SourceId => inner.SourceId;

    public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
        CancellationToken cancellationToken
    ) => inner.DescribeAsync(cancellationToken);

    public async ValueTask<CommandResult> InvokeAsync(
        CommandInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(invocation);
        string commandId = FormattableString.Invariant(
            $"ext:{extensionId}:{invocation.Name.TrimStart('/')}"
        );
        using CommandInvocationLease lease = leases.Enter(extensionId, commandId);
        if (lease.Cancelled)
        {
            // The extension was unloading when the command arrived (a stale registry snapshot):
            // nothing is started, and the unload notice already reported it.
            return CommandResult.None;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lease.Token
        );
        try
        {
            CommandResult result = await inner
                .InvokeAsync(invocation, linked.Token)
                .ConfigureAwait(false);
            // A result that arrives after the unload is discarded, never rendered (7.1).
            return lease.Cancelled ? CommandResult.None : result;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && lease.Cancelled)
        {
            // The unload cancelled this invocation; its notice is the user's report, and the
            // caller's own cancellation still propagates through the filter above.
            return CommandResult.None;
        }
        catch (ObjectDisposedException) when (lease.Cancelled)
        {
            leases.ReportLateHostCall(commandId, extensionId);
            return CommandResult.None;
        }
    }
}

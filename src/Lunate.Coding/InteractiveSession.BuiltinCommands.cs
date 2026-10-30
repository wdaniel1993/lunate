using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// The built-in commands as the registry's first source. The dispatch switch lives here, as
    /// this source's implementation, so the interactive session has no second dispatch path. The
    /// <c>skill:&lt;name&gt;</c> placeholder stays in the table for <c>/help</c>; an unknown skill
    /// name falls back to it and keeps the existing unknown-skill notice.
    /// </summary>
    private sealed class BuiltinCommandSource(InteractiveSession session) : ICommandSource
    {
        public string SourceId => Commands.BuiltinSourceId;

        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(Commands.PublicDescriptors);

        public async ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(invocation);
            string name = "/" + invocation.Name;
            string skillPrefix = Commands.SkillPrefix.TrimStart('/');
            if (invocation.Name.StartsWith(skillPrefix, StringComparison.Ordinal))
            {
                await session
                    .InvokeSkillAsync(
                        invocation.Name[skillPrefix.Length..],
                        invocation.Argument,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return CommandResult.None;
            }

            switch (name)
            {
                case "/quit":
                    session.Quit();
                    return CommandResult.None;
                case "/help":
                    session.ShowHelp();
                    return CommandResult.None;
                case "/compact":
                    if (session.RefusedWhileBusy())
                    {
                        return CommandResult.None;
                    }

                    if (invocation.Argument.Length > CompactInstructionsLimit)
                    {
                        session.Notify(
                            FormattableString.Invariant(
                                $"compact instructions are limited to {CompactInstructionsLimit:N0} characters"
                            ),
                            NoticeLevel.Warning
                        );
                        return CommandResult.None;
                    }

                    bool compacted = await session
                        ._harness!.CompactNowAsync(
                            invocation.Argument.Length == 0 ? null : invocation.Argument,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    session.Notify(compacted ? "context compacted" : "nothing to compact");
                    return CommandResult.None;
                case "/tree":
                    session.OpenTree();
                    return CommandResult.None;
                case "/copy":
                    await session.CopyLastAnswerAsync(cancellationToken).ConfigureAwait(false);
                    return CommandResult.None;
                case "/editor":
                    // The same round-trip Ctrl+G runs: the command is the way in when the terminal
                    // does not deliver BEL or a keybindings file claims the chord.
                    await session.RunEditorAsync(cancellationToken).ConfigureAwait(false);
                    return CommandResult.None;
                case "/fullscreen":
                    // The same round-trip Ctrl+O runs: the command is the way in when the terminal
                    // does not deliver the chord or a keybindings file claims it.
                    session.OpenViewer();
                    return CommandResult.None;
                case "/export":
                    session.ExportSession(invocation.Argument);
                    return CommandResult.None;
                case "/debug":
                    session.WriteDiagnosticBundle(invocation.Argument);
                    return CommandResult.None;
                case "/fork":
                    session.ForkSession(invocation.Argument);
                    return CommandResult.None;
                case "/clone":
                    session.CloneSession();
                    return CommandResult.None;
                case "/rewind":
                    await session
                        .RewindAsync(invocation.Argument, cancellationToken)
                        .ConfigureAwait(false);
                    return CommandResult.None;
                case "/context":
                    session.Notify(session.ContextText());
                    return CommandResult.None;
                case "/name":
                    session.SetSessionName(invocation.Argument);
                    return CommandResult.None;
                case "/session":
                    session.ShowSessionReport();
                    return CommandResult.None;
                case "/settings":
                    if (invocation.Argument.Length == 0)
                    {
                        session.ShowSettings();
                        return CommandResult.None;
                    }

                    session.SetSetting(invocation.Argument);
                    return CommandResult.None;
                case "/skills":
                    session.Notify(session.SkillsText());
                    return CommandResult.None;
                case "/mcp":
                    session.Notify(
                        await session.McpStatusTextAsync(cancellationToken).ConfigureAwait(false)
                    );
                    return CommandResult.None;
                case "/extensions":
                    if (invocation.Argument.StartsWith("reload ", StringComparison.Ordinal))
                    {
                        await session
                            .ReloadExtension(
                                invocation.Argument["reload ".Length..].Trim(),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        return CommandResult.None;
                    }

                    if (invocation.Argument.StartsWith("restart ", StringComparison.Ordinal))
                    {
                        await session
                            .RestartExtension(
                                invocation.Argument["restart ".Length..].Trim(),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        return CommandResult.None;
                    }

                    if (invocation.Argument.Length > 0)
                    {
                        session.Notify(
                            "usage: /extensions or /extensions reload <id> or /extensions restart <id>"
                        );
                        return CommandResult.None;
                    }

                    session.Notify(session.ExtensionsText());
                    return CommandResult.None;
                case "/trust":
                    session.HandleTrustCommand(invocation.Argument);
                    return CommandResult.None;
                case "/model":
                    if (session.RefusedWhileBusy())
                    {
                        return CommandResult.None;
                    }

                    if (invocation.Argument.Length > 0)
                    {
                        await session
                            .SwitchModel(invocation.Argument, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        session.OpenModelPicker();
                    }

                    return CommandResult.None;
                case "/favorite":
                    session.Favorite(invocation.Argument);
                    return CommandResult.None;
                case "/unfavorite":
                    session.Unfavorite(invocation.Argument);
                    return CommandResult.None;
                case "/auth":
                    if (invocation.Argument.Length == 0)
                    {
                        session.OpenAuthPicker();
                    }
                    else
                    {
                        await session
                            .LoginAsync(invocation.Argument, cancellationToken, "/auth")
                            .ConfigureAwait(false);
                    }

                    return CommandResult.None;
                case "/login":
                    await session
                        .LoginAsync(invocation.Argument, cancellationToken)
                        .ConfigureAwait(false);
                    return CommandResult.None;
                case "/logout":
                    session.Logout(invocation.Argument);
                    return CommandResult.None;
                case "/new":
                    if (session.RefusedWhileBusy())
                    {
                        return CommandResult.None;
                    }

                    session.StartNewSession();
                    return CommandResult.None;
                case "/thinking":
                    if (invocation.Argument.Length == 0)
                    {
                        // Read-only: the bare form reports the level and the mapping even mid-turn.
                        session.Notify(session.ThinkingText());
                        return CommandResult.None;
                    }

                    if (session.RefusedWhileBusy())
                    {
                        return CommandResult.None;
                    }

                    session.SetThinkingLevel(invocation.Argument);
                    return CommandResult.None;
                case "/resume":
                    if (session.RefusedWhileBusy())
                    {
                        return CommandResult.None;
                    }

                    session.OpenSessionPicker();
                    return CommandResult.None;
                default:
                    return CommandResult.Error("unknown command: " + name);
            }
        }
    }
}

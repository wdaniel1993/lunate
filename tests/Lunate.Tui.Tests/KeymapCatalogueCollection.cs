namespace Lunate.Tui.Tests;

/// <summary>
/// The keymap is static: <see cref="Lunate.Tui.Interaction.KeyBindingCatalog.All"/> is replaced in
/// place by <c>Remap</c>. Two classes touching it must never run at the same time — a router test
/// asserting that Ctrl+D quits can observe the moment a remap test has moved that binding. That race
/// is silent on a fast machine and fatal on a slower one, which is how it surfaced on the Windows leg.
/// </summary>
[CollectionDefinition("keymap-catalogue", DisableParallelization = true)]
public sealed class KeymapCatalogueCollection;

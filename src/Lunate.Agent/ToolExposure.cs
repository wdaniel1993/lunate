namespace Lunate.Agent;

/// <summary>Where a tool is visible: the model, nested execution, both or neither.</summary>
public enum ToolExposure
{
    /// <summary>Declared to the model and callable from nested execution; the default.</summary>
    Direct,

    /// <summary>Declared to the model only; nested execution refuses it.</summary>
    ModelOnly,

    /// <summary>Callable from nested execution only; never declared to the model.</summary>
    Programmatic,

    /// <summary>Discoverable but not declared upfront (code mode); nested execution refines this later.</summary>
    Deferred,

    /// <summary>Internal; neither declared nor callable programmatically.</summary>
    Hidden,
}

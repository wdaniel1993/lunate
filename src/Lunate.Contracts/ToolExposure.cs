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

    /// <summary>
    /// Never declared to the model; discoverable through the run's tool stubs and callable from
    /// nested execution (code mode).
    /// </summary>
    Deferred,

    /// <summary>Internal; neither declared nor callable programmatically.</summary>
    Hidden,
}

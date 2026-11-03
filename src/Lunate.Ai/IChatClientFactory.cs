using Microsoft.Extensions.AI;

namespace Lunate.Ai;

public interface IChatClientFactory
{
    IChatClient Create(ModelInfo model);

    /// <summary>
    /// Creates the client with an explicit credential, bypassing the environment/auth-store
    /// resolution: declared extension providers hand the host-read secret in here, so the value
    /// only ever reaches the client's request headers.
    /// </summary>
    IChatClient Create(ModelInfo model, string? apiKey);

    /// <summary>
    /// Creates the client with the session's request identity: its id names the wire headers'
    /// {session.id} (and {lunate.version}); the run's {run.id} comes from the active run.
    /// </summary>
    virtual IChatClient Create(ModelInfo model, RequestContext? context) =>
        Create(model, apiKey: null);

    /// <summary>
    /// Creates the client with an explicit credential and the session's request identity.
    /// </summary>
    virtual IChatClient Create(ModelInfo model, string? apiKey, RequestContext? context) =>
        Create(model, apiKey);
}

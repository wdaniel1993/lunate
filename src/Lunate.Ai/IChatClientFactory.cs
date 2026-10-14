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
}

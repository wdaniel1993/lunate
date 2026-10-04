using Microsoft.Extensions.AI;

namespace Lunate.Ai;

public interface IChatClientFactory
{
    IChatClient Create(ModelInfo model);
}

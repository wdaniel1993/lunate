namespace Lunate.Coding;

/// <summary>
/// Raised when a model id does not resolve in the catalog: a usage error when the id came from
/// <c>--model</c>, a configuration error when it came from settings or the environment.
/// </summary>
internal sealed class UnknownModelException(string message) : Exception(message);

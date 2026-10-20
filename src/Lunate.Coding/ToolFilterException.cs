namespace Lunate.Coding;

/// <summary>
/// A launch tool selection that names tools the composed registry does not offer. The message is
/// the usage error; frontends that distinguish usage failures report it without a model call.
/// </summary>
internal sealed class ToolFilterException(string message) : Exception(message);

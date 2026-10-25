using Lunate.Extensibility;
using Lunate.Extensibility.Compile;

namespace Lunate.Coding;

/// <summary>
/// The production compile seam, shaped like <see cref="SingleFileRestoreFactory"/>: a factory, not
/// a compiler, so composing a session constructs nothing and the Roslyn assemblies a compile needs
/// are loaded by the first build (and by nothing on the startup path).
/// </summary>
internal static class SingleFileCompileFactory
{
    internal static Func<ISingleFileCompiler?> Compiler { get; } =
        static () => new RoslynSingleFileCompiler();
}

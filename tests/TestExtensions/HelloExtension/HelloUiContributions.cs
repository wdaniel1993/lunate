using Lunate.Extensibility.Abstractions;

namespace HelloExtension;

public sealed class HelloToolResultRenderer(IExtensionLog log) : IToolResultRenderer
{
    public string ToolName => "hello-tool";

    public IReadOnlyList<TextSegment> Render(ToolResultView view)
    {
        log.Info($"hello renderer rendered {view.ToolName} call {view.CallId}");
        return [new TextSegment($"hello-rendered {view.ResultText}", TextStyle.Dim)];
    }
}

public sealed class HelloStatusWidget(IExtensionLog log) : IStatusWidget
{
    public IReadOnlyList<TextSegment> Segments(StatusView view)
    {
        log.Info($"hello widget contributed for {view.Model}");
        return [new TextSegment("hello-widget", TextStyle.Accent)];
    }
}

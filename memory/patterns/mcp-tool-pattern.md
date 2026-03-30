# Pattern: Adding an MCP Tool

MCP tools are thin wrappers around MediatR handlers. They live in `backend/src/Anela.Heblo.API/MCP/Tools/`.

## Structure

```csharp
[McpServerToolType]
public class MyFeatureTool
{
    private readonly IMediator _mediator;

    public MyFeatureTool(IMediator mediator) => _mediator = mediator;

    [McpServerTool]
    [Description("What this tool does")]
    public async Task<string> ToolName(
        [Description("param description")] string param)
    {
        var result = await _mediator.Send(new MyRequest { Param = param });
        return JsonSerializer.Serialize(result);
    }
}
```

## Rules
- Errors: throw `McpException` (from `ModelContextProtocol` namespace)
- Return `Task<string>` with JSON-serialized response
- Register in `McpModule.cs` via `.WithTools<MyFeatureTool>()`
- Tests go in `backend/test/Anela.Heblo.Tests/MCP/Tools/` — see existing tests for patterns
- Authentication is handled by existing Microsoft Entra ID middleware (no extra work needed)

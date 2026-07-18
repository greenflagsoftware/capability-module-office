using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AgentDock.Office.Tools;

// Delete this class once the module has a real tool — it only proves the MCP endpoint is wired up.
[McpServerToolType]
public static class ExampleTool
{
    [McpServerTool, Description("Echoes the input back. Replace with a real tool.")]
    public static string Echo([Description("Text to echo back")] string message)
        => $"Office echo: {message}";
}

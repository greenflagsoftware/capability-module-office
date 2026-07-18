using AgentDock.ModuleName.Tools;

namespace AgentDock.ModuleName.Tests;

public class ExampleToolTests
{
    [Fact]
    public void Echo_ReturnsMessagePrefixedWithModuleName()
    {
        var result = ExampleTool.Echo("hello");

        Assert.Equal("ModuleName echo: hello", result);
    }
}

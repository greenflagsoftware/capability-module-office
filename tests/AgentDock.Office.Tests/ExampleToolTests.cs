using AgentDock.Office.Tools;

namespace AgentDock.Office.Tests;

public class ExampleToolTests
{
    [Fact]
    public void Echo_ReturnsMessagePrefixedWithOffice()
    {
        var result = ExampleTool.Echo("hello");

        Assert.Equal("Office echo: hello", result);
    }
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Xunit;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class McpConfigWriterTests
{
    [Fact]
    public void WriteConfigFile_NullOrEmptyList_ReturnsNull()
    {
        Assert.Null(McpConfigWriter.WriteConfigFile(new List<McpServerConfig>()));
    }

    [Fact]
    public void WriteConfigFile_ValidServers_CreatesJsonConfigFile()
    {
        var servers = new List<McpServerConfig>
        {
            new("test-server", "node", new List<string> { "server.js" }, new Dictionary<string, string> { { "ENV_VAR", "TEST" } })
        };

        var filePath = McpConfigWriter.WriteConfigFile(servers);
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath));

        try
        {
            var content = File.ReadAllText(filePath!);
            var json = JsonNode.Parse(content);
            Assert.NotNull(json);

            var serverNode = json!["mcpServers"]?["test-server"];
            Assert.NotNull(serverNode);
            Assert.Equal("node", serverNode!["command"]?.ToString());
            Assert.Equal("server.js", serverNode!["args"]?[0]?.ToString());
            Assert.Equal("TEST", serverNode!["env"]?["ENV_VAR"]?.ToString());
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}

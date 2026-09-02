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

    [Fact]
    public async Task AgentSession_Dispose_CleansUpRegisteredTempFiles()
    {
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"tendril-test-temp-{Guid.NewGuid():N}.tmp");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"tendril-test-temp-{Guid.NewGuid():N}.tmp");

        File.WriteAllText(tempFile1, "temporary data 1");
        File.WriteAllText(tempFile2, "temporary data 2");

        Assert.True(File.Exists(tempFile1));
        Assert.True(File.Exists(tempFile2));

        var spec = new AgentProcessSpec
        {
            FileName = "echo",
            Arguments = ["hello"],
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            TempFiles = [tempFile1, tempFile2],
        };

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };
        process.Start();

        var session = new Ivy.Tendril.Agents.Runtime.AgentSession(
            process,
            new Ivy.Tendril.Agents.Providers.Antigravity.AntigravityEventParser(),
            "antigravity",
            "test-session");

        await session.StartAsync(spec, CancellationToken.None);
        await session.WaitForCompletionAsync();
        await session.DisposeAsync();

        Assert.False(File.Exists(tempFile1), "tempFile1 should be deleted upon session completion/disposal");
        Assert.False(File.Exists(tempFile2), "tempFile2 should be deleted upon session completion/disposal");
    }

    [Fact]
    public void AntigravityCli_BuildProcessSpec_RegistersTempFiles()
    {
        var cli = new Ivy.Tendril.Agents.Providers.Antigravity.AntigravityCli();
        var config = new AgentLaunchConfig
        {
            Prompt = "Test prompt",
            WorkingDirectory = Path.GetTempPath(),
            McpServers =
            [
                new("test-server", "node", new List<string> { "server.js" })
            ]
        };

        var spec = cli.BuildProcessSpec(config);

        try
        {
            Assert.NotEmpty(spec.TempFiles);
            Assert.All(spec.TempFiles, file => Assert.True(File.Exists(file)));
            Assert.Contains(spec.TempFiles, f => f.Contains("tendril-mcp-"));
            Assert.Contains(spec.TempFiles, f => f.Contains("tendril-agy-prompt-"));
        }
        finally
        {
            foreach (var file in spec.TempFiles)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }
}

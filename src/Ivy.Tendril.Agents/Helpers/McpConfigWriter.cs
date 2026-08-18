using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public static class McpConfigWriter
{
    public static string? WriteConfigFile(IReadOnlyList<McpServerConfig> mcpServers)
    {
        if (mcpServers == null || mcpServers.Count == 0) return null;

        var configurable = mcpServers.Where(s => !string.IsNullOrWhiteSpace(s.Command)).ToList();
        if (configurable.Count == 0) return null;

        var mcpObj = new JsonObject();
        var serversObj = new JsonObject();

        foreach (var server in configurable)
        {
            var serverNode = new JsonObject
            {
                ["command"] = server.Command
            };

            if (server.Arguments is { Count: > 0 })
            {
                var argsArr = new JsonArray();
                foreach (var arg in server.Arguments)
                    argsArr.Add(arg);
                serverNode["args"] = argsArr;
            }

            if (server.Environment is { Count: > 0 })
            {
                var envObj = new JsonObject();
                foreach (var (k, v) in server.Environment)
                    envObj[k] = v;
                serverNode["env"] = envObj;
            }

            serversObj[server.Name] = serverNode;
        }

        mcpObj["mcpServers"] = serversObj;

        var tempPath = Path.Combine(Path.GetTempPath(), $"tendril-mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, mcpObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return tempPath;
    }
}

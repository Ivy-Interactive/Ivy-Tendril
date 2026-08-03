using System.Collections.Generic;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Memory;

public interface IMemoryService
{
    string ResolveVaultPath(string? workspaceDir = null, string? projectName = null);
    VaultStatusInfo GetStatus(string? workspaceDir = null, string? projectName = null);
    IEnumerable<MemoryNote> ListMemories(string? workspaceDir = null, string? projectName = null);
    MemoryNote? ReadMemory(string noteName, string? workspaceDir = null, string? projectName = null);
    MemoryNote AddMemory(string name, string? title = null, IEnumerable<string>? tags = null, string? content = null, string? workspaceDir = null, string? projectName = null);
    void WriteMemory(string noteName, string content, string? workspaceDir = null, string? projectName = null);
    void LinkFile(string noteName, string relativeFilePath, string? workspaceDir = null, string? projectName = null);
    void UpdateMemory(string noteName, string? workspaceDir = null, string? projectName = null);
    void RelateMemories(string sourceNote, string targetNote, string? workspaceDir = null, string? projectName = null);
    void DeleteMemory(string noteName, string? workspaceDir = null, string? projectName = null);
    IEnumerable<MemoryNote> QueryMemories(string keyword, string? workspaceDir = null, string? projectName = null);
    string GetRulesMarkdown(string? workspaceDir = null, string? projectName = null);
}

using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class KiroConfigurator : JsonFileMcpConfigurator
    {
        public KiroConfigurator() : base(new McpClient
        {
            name = "Kiro",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro", "settings", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro", "settings", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kiro", "settings", "mcp.json"),
            EnsureEnvObject = true,
            DefaultUnityFields = { { "disabled", false } }
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Kiro。",
            "进入 File > Settings > Settings，搜索 \"MCP\" 并打开 Workspace MCP Config，或打开上方配置文件路径。",
            "粘贴 JSON 配置内容。",
            "保存后重启 Kiro。"
        };
    }
}

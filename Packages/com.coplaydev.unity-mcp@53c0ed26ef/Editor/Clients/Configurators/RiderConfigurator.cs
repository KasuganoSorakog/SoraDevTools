using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class RiderConfigurator : JsonFileMcpConfigurator
    {
        public RiderConfigurator() : base(new McpClient
        {
            name = "Rider GitHub Copilot",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JetBrains", "Rider", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "JetBrains", "Rider", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "JetBrains", "Rider", "mcp.json"),
            IsVsCodeLayout = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "在 Rider 中安装 GitHub Copilot 插件。",
            "打开或创建上方路径中的 mcp.json。",
            "粘贴 JSON 配置内容。",
            "保存后重启 Rider。"
        };
    }
}


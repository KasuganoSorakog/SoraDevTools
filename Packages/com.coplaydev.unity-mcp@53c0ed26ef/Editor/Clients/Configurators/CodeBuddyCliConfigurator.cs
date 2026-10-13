using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    /// <summary>
    /// Configures the CodeBuddy CLI (~/.codebuddy.json) MCP settings.
    /// </summary>
    public class CodeBuddyCliConfigurator : JsonFileMcpConfigurator
    {
        public CodeBuddyCliConfigurator() : base(new McpClient
        {
            name = "CodeBuddy CLI",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codebuddy.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codebuddy.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codebuddy.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "安装 CodeBuddy CLI，并确认 '~/.codebuddy.json' 已存在。",
            "点击“配置”添加 UnityMCP 项，或手动编辑上方文件。",
            "如有需要，重启 CLI 会话。"
        };
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ClaudeCodeConfigurator : JsonFileMcpConfigurator
    {
        public ClaudeCodeConfigurator() : base(new McpClient
        {
            name = "Claude Code",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
            SupportsHttpTransport = true,
            HttpUrlProperty = "url", // Claude Code uses "url" for HTTP servers
            IsVsCodeLayout = false,  // Claude Code uses standard mcpServers layout
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "在 Claude Code 中打开当前项目。",
            "点击本面板的“配置”按钮，或手动编辑 ~/.claude.json。",
            "UnityMCP 会写入全局 mcpServers 配置段。",
            "重启 Claude Code 使配置生效。"
        };
    }
}

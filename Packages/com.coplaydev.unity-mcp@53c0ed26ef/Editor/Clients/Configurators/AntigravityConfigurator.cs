using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class AntigravityConfigurator : JsonFileMcpConfigurator
    {
        public AntigravityConfigurator() : base(new McpClient
        {
            name = "Antigravity",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "mcp_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "mcp_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "mcp_config.json"),
            HttpUrlProperty = "serverUrl",
            DefaultUnityFields = { { "disabled", false } },
            StripEnvWhenNotRequired = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Antigravity。",
            "在 Agent 面板点击 more_horiz 菜单 > MCP Servers。",
            "选择 Unity MCP 的 Install，或直接使用上方“配置”按钮。",
            "如有需要，重启 Antigravity。"
        };
    }
}

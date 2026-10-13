using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class WindsurfConfigurator : JsonFileMcpConfigurator
    {
        public WindsurfConfigurator() : base(new McpClient
        {
            name = "Windsurf",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf", "mcp_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf", "mcp_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codeium", "windsurf", "mcp_config.json"),
            HttpUrlProperty = "serverUrl",
            DefaultUnityFields = { { "disabled", false } },
            StripEnvWhenNotRequired = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Windsurf。",
            "进入 File > Preferences > Windsurf Settings > MCP > Manage MCPs > View raw config，或打开上方配置文件路径。",
            "粘贴 JSON 配置内容。",
            "保存后重启 Windsurf。"
        };
    }
}

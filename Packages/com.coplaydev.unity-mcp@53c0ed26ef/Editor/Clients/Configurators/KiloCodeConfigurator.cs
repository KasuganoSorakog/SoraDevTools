using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class KiloCodeConfigurator : JsonFileMcpConfigurator
    {
        public KiloCodeConfigurator() : base(new McpClient
        {
            name = "Kilo Code",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "globalStorage", "kilocode.kilo-code", "settings", "mcp_settings.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User", "globalStorage", "kilocode.kilo-code", "settings", "mcp_settings.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code", "User", "globalStorage", "kilocode.kilo-code", "settings", "mcp_settings.json"),
            IsVsCodeLayout = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "在 VS Code 中安装 Kilo Code 扩展。",
            "打开 Kilo Code 设置（侧边栏齿轮图标）。",
            "进入 MCP Servers 区域并点击 Edit Global MCP Settings，或打开上方配置文件路径。",
            "将 JSON 配置粘贴到 mcpServers 对象中。",
            "保存后重启 VS Code。"
        };
    }
}

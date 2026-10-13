using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class TraeConfigurator : JsonFileMcpConfigurator
    {
        public TraeConfigurator() : base(new McpClient
        {
            name = "Trae",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Trae", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Trae", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Trae", "mcp.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Trae，进入 Settings > MCP。",
            "选择 Add Server > Add Manually。",
            "粘贴 JSON 配置，或指向 mcp.json 文件：\n"+
                "Windows: %AppData%\\Trae\\mcp.json\n" +
                "macOS: ~/Library/Application Support/Trae/mcp.json\n" +
                "Linux: ~/.config/Trae/mcp.json\n",
            "保存后重启 Trae。"
        };
    }
}

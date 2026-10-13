using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class CursorConfigurator : JsonFileMcpConfigurator
    {
        public CursorConfigurator() : base(new McpClient
        {
            name = "Cursor",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "mcp.json")
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Cursor。",
            "进入 File > Preferences > Cursor Settings > MCP > Add new global MCP server，或打开上方配置文件路径。",
            "粘贴 JSON 配置内容。",
            "保存后重启 Cursor。"
        };
    }
}

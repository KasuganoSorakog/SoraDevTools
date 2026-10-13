using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class VSCodeConfigurator : JsonFileMcpConfigurator
    {
        public VSCodeConfigurator() : base(new McpClient
        {
            name = "VSCode GitHub Copilot",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Code", "User", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Code", "User", "mcp.json"),
            IsVsCodeLayout = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "安装 GitHub Copilot 扩展。",
            "打开或创建上方路径中的 mcp.json。",
            "粘贴 JSON 配置内容。",
            "保存后重启 VSCode。"
        };
    }
}

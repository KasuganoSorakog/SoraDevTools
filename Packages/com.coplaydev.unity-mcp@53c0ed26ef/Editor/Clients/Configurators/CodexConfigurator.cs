using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class CodexConfigurator : CodexMcpConfigurator
    {
        public CodexConfigurator() : base(new McpClient
        {
            name = "Codex",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml")
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "在终端运行 'codex config edit'，或打开上方配置文件路径。",
            "粘贴 TOML 配置内容。",
            "保存后重启 Codex。"
        };
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class ClaudeDesktopConfigurator : JsonFileMcpConfigurator
    {
        public const string ClientName = "Claude Desktop";

        public ClaudeDesktopConfigurator() : base(new McpClient
        {
            name = ClientName,
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude_desktop_config.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Claude", "claude_desktop_config.json"),
            SupportsHttpTransport = false,
            StripEnvWhenNotRequired = true
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Claude Desktop。",
            "进入 Settings > Developer > Edit Config，或打开上方配置文件路径。",
            "粘贴 JSON 配置内容。",
            "保存后重启 Claude Desktop。"
        };

        public override void Configure()
        {
            bool useHttp = McpProjectSettings.GetUseHttpTransport();
            if (useHttp)
            {
                throw new InvalidOperationException("Claude Desktop 不支持 HTTP 连接方式。请先在设置中切换为 stdio，再执行配置。");
            }

            base.Configure();
        }

        public override string GetManualSnippet()
        {
            bool useHttp = McpProjectSettings.GetUseHttpTransport();
            if (useHttp)
            {
                return "# Claude Desktop 不支持 HTTP 连接方式。\n" +
                       "# 请切换为 Stdio 本地模式后重新生成配置。";
            }

            return base.GetManualSnippet();
        }
    }
}

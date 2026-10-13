using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using UnityEditor;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class CherryStudioConfigurator : JsonFileMcpConfigurator
    {
        public const string ClientName = "Cherry Studio";

        public CherryStudioConfigurator() : base(new McpClient
        {
            name = ClientName,
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cherry Studio", "config"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Cherry Studio", "config"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Cherry Studio", "config"),
            SupportsHttpTransport = false
        })
        { }

        public override bool SupportsAutoConfigure => false;

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "打开 Cherry Studio。",
            "进入 Settings > MCP Server。",
            "点击 Add Server。",
            "STDIO 模式推荐填写：",
            "  - 名称: unity-mcp",
            "  - 类型: STDIO",
            "  - 命令: uvx",
            "  - 参数: 从下方手动配置 JSON 中复制 args。",
            "保存后重启 Cherry Studio。",
            "",
            "说明：Cherry Studio 需要在自己的界面中手动配置。",
            "下方配置片段可作为填写参数的参考。"
        };

        public override McpStatus CheckStatus(bool attemptAutoRewrite = true)
        {
            client.SetStatus(McpStatus.NotConfigured, "Cherry Studio 需要在客户端界面中手动配置");
            return client.status;
        }

        public override void Configure()
        {
            throw new InvalidOperationException(
                "Cherry Studio 需要在客户端界面中手动配置。请参考手动配置片段和操作步骤。"
            );
        }

        public override string GetManualSnippet()
        {
            bool useHttp = McpProjectSettings.GetUseHttpTransport();

            if (useHttp)
            {
                return "# Cherry Studio 不支持当前 HTTP 连接方式。\n" +
                       "# Cherry Studio 支持 STDIO 和 SSE。\n" +
                       "# \n" +
                       "# 使用 Cherry Studio 时：\n" +
                       "# 1. 将连接方式切换为 Stdio 本地。\n" +
                       "# 2. 回到当前配置界面。\n" +
                       "# 3. 复制重新生成的 STDIO 配置片段。\n" +
                       "# \n" +
                       "# SSE 模式当前未接入 UnityMCP。";
            }

            return base.GetManualSnippet() + "\n\n" +
                   "# Cherry Studio 手动配置说明：\n" +
                   "# Cherry Studio 需要在客户端界面填写，不是直接写 JSON 文件。\n" +
                   "# \n" +
                   "# 1. 打开 Cherry Studio。\n" +
                   "# 2. 进入 Settings > MCP Server。\n" +
                   "# 3. 点击 Add Server。\n" +
                   "# 4. 按上方 JSON 填写：\n" +
                   "#    - Name: unity-mcp\n" +
                   "#    - Type: STDIO\n" +
                   "#    - Command: 复制 JSON 中的 command 值。\n" +
                   "#    - Arguments: 复制 args 数组，可按空格分隔或逐项填写。\n" +
                   "#    - Active: true\n" +
                   "# 5. 保存后重启 Cherry Studio。";
        }
    }
}

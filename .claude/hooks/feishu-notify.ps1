# 飞书自建应用机器人 - Claude Code 任务完成通知脚本
# 用法: powershell -ExecutionPolicy Bypass -File feishu-notify.ps1

# ===== 开关检查 =====
# 在 hooks 目录下存在 .notify_on 文件时才发送通知
# 开启: New-Item "$PSScriptRoot\.notify_on"
# 关闭: Remove-Item "$PSScriptRoot\.notify_on"
$switchFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".notify_on"
if (-not (Test-Path $switchFile)) {
    exit 0
}

# ===== 配置区域 =====
$APP_ID       = "cli_a90314d54e38dbd9"
$APP_SECRET   = "HtzCSquRtWLzuM6Z8TQFOhfVr6YYIfXt"
$USER_OPEN_ID = "ou_187f7be2c2a56e05f6685f9ec0651eda"
# =====================

# ===== 读取 hook 输入 =====
$hookInput = [Console]::In.ReadToEnd()
try {
    $hookData = $hookInput | ConvertFrom-Json
    $sessionId = $hookData.session_id
    $cwd = $hookData.cwd
    $transcriptPath = $hookData.transcript_path
} catch {
    $sessionId = "unknown"
    $cwd = (Get-Location).Path
    $transcriptPath = ""
}

$hooksDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$projectName = Split-Path $cwd -Leaf
$shortSession = if ($sessionId.Length -ge 8) { $sessionId.Substring(0, 8) } else { $sessionId }

# ===== 从 transcript 提取最后的用户请求 =====
$lastUserMessage = ""
if ($transcriptPath -and (Test-Path $transcriptPath)) {
    try {
        $lines = Get-Content $transcriptPath -Tail 80 -Encoding utf8
        for ($i = $lines.Count - 1; $i -ge 0; $i--) {
            try {
                $entry = $lines[$i] | ConvertFrom-Json
                if ($entry.type -eq "user" -and $entry.message -and $entry.message.role -eq "user") {
                    $msg = ""
                    if ($entry.message.content -is [string]) {
                        $msg = $entry.message.content
                    } elseif ($entry.message.content -is [array]) {
                        foreach ($block in $entry.message.content) {
                            if ($block.type -eq "text" -and $block.text) {
                                $msg = $block.text
                                break
                            }
                        }
                    }
                    $msg = $msg.Trim()
                    # 跳过系统消息和命令消息
                    if ($msg.StartsWith("<")) { continue }
                    if ($msg.Length -gt 0) {
                        if ($msg.Length -gt 100) {
                            $msg = $msg.Substring(0, 100) + "..."
                        }
                        $lastUserMessage = $msg
                        break
                    }
                }
            } catch { continue }
        }
    } catch {
        $lastUserMessage = ""
    }
}
if (-not $lastUserMessage) {
    $lastUserMessage = "(无法获取)"
}

# ===== 从 transcript 提取 Claude 最后的回复作为任务总结 =====
$taskSummary = ""
if ($transcriptPath -and (Test-Path $transcriptPath)) {
    try {
        $lines = Get-Content $transcriptPath -Tail 80 -Encoding utf8
        for ($i = $lines.Count - 1; $i -ge 0; $i--) {
            try {
                $entry = $lines[$i] | ConvertFrom-Json
                if ($entry.message -and $entry.message.role -eq "assistant" -and $entry.message.content) {
                    # 提取 assistant 消息中的文本内容
                    $texts = @()
                    if ($entry.message.content -is [array]) {
                        foreach ($block in $entry.message.content) {
                            if ($block.type -eq "text" -and $block.text) {
                                $texts += $block.text.Trim()
                            }
                        }
                    } elseif ($entry.message.content -is [string]) {
                        $texts += $entry.message.content.Trim()
                    }
                    $summary = ($texts -join "`n").Trim()
                    if ($summary.Length -gt 0) {
                        if ($summary.Length -gt 200) {
                            $summary = $summary.Substring(0, 200) + "..."
                        }
                        $taskSummary = $summary
                        break
                    }
                }
            } catch { continue }
        }
    } catch {}
}
if (-not $taskSummary) {
    $taskSummary = "(无法获取)"
}

# ===== 获取 tenant_access_token =====
$tokenBody = @{
    app_id     = $APP_ID
    app_secret = $APP_SECRET
} | ConvertTo-Json -Compress

try {
    $tokenResp = Invoke-RestMethod -Uri "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal" `
        -Method Post -ContentType "application/json; charset=utf-8" -Body $tokenBody
    if (-not $tokenResp.tenant_access_token) { exit 0 }
    $token = $tokenResp.tenant_access_token
} catch { exit 0 }

# ===== 构造飞书卡片消息 =====
$card = @{
    config = @{ wide_screen_mode = $true }
    header = @{
        title = @{ content = "Claude Code · $projectName"; tag = "plain_text" }
        template = "green"
    }
    elements = @(
        @{
            tag = "div"
            fields = @(
                @{ is_short = $true; text = @{ tag = "lark_md"; content = "**会话** $shortSession" } }
                @{ is_short = $true; text = @{ tag = "lark_md"; content = "**时间** $timestamp" } }
            )
        }
        @{ tag = "hr" }
        @{
            tag = "markdown"
            content = "**📝 请求**  $lastUserMessage`n**✅ 总结**  $taskSummary"
        }
        @{
            tag = "note"
            elements = @(
                @{ tag = "plain_text"; content = "$cwd" }
            )
        }
    )
} | ConvertTo-Json -Depth 10 -Compress

# ===== 发送卡片消息 =====
$sendBody = @{
    receive_id = $USER_OPEN_ID
    msg_type   = "interactive"
    content    = $card
} | ConvertTo-Json -Compress -Depth 5

$sendUri = "https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=open_id"

try {
    $sendResp = Invoke-RestMethod -Uri $sendUri `
        -Method Post -ContentType "application/json; charset=utf-8" `
        -Headers @{ Authorization = "Bearer $token" } `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($sendBody))
} catch {}

exit 0

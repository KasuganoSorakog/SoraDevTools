# FeiShuTools - 飞书机器人 Unity 插件

Unity Editor 中发送飞书消息的轻量工具，支持群聊、个人、手机号等多种发送方式。

## 目录结构

```
Assets/Plugins/FeiShuTools/
├── FeiShuBot.cs              # 核心 API 类
├── Editor/
│   └── FeiShuTestWindow.cs   # 编辑器测试窗口
└── README.md                 # 本文档
```

## 前置条件

### 1. 创建飞书应用

前往 [飞书开放平台](https://open.feishu.cn/) 创建应用，获取：

- **App ID** — 应用唯一标识
- **App Secret** — 应用密钥

### 2. 开启权限

在飞书开放平台 → 你的应用 → **权限管理** 中开启：

| 权限 | 权限标识 | 用途 |
|------|---------|------|
| 以应用身份发消息 | `im:message:send_as_bot` | 发送消息（必须） |
| 获取群组信息 | `im:chat:readonly` | 查询群列表（可选） |
| 通过手机号获取用户 ID | `contact:user.id:readonly` | 手机号发送（可选） |

### 3. 发布应用

权限开通后，必须 **创建版本并发布** 才能生效：

> 飞书开放平台 → 你的应用 → 应用发布 → 版本管理与发布 → 创建版本 → 发布

### 4. 机器人入群（群聊场景）

如需向群聊发消息，需将机器人添加到群中：

> 飞书 → 目标群 → 群设置 → 群机器人 → 添加机器人 → 搜索你的应用名

## 编辑器窗口使用

菜单栏：**Tools → 飞书 → 发送测试消息**

窗口提供可视化操作界面，支持：

- 通过 ID（chat_id / open_id / email）发送
- 通过手机号发送
- 查询机器人所在的群列表

## 代码调用

### 基本用法

```csharp
using SoraTools;

var bot = new FeiShuBot("你的AppID", "你的AppSecret");

// 发送到群聊
bot.SendText("chat_id", "oc_xxxxx", "Hello from Unity!");

// 发送给个人（通过 open_id）
bot.SendText("open_id", "ou_xxxxx", "你好");

// 发送给个人（通过邮箱）
bot.SendText("email", "user@company.com", "你好");
```

### 通过手机号发送

```csharp
// 自动查询 open_id 再发送，无需 +86 前缀
bot.SendTextByPhone("13800138000", "任务完成通知");
```

### 查询手机号对应的 open_id

```csharp
string openId = bot.GetOpenIdByPhone("13800138000");
```

### 获取机器人所在的群列表

```csharp
string json = bot.ListChats();
Debug.Log(json); // 返回包含 chat_id、群名等信息的 JSON
```

### 实际场景示例

```csharp
// 构建完成后通知
void OnBuildComplete(bool success)
{
    var bot = new FeiShuBot("cli_xxx", "secret_xxx");
    var status = success ? "成功" : "失败";
    bot.SendText("chat_id", "oc_xxxxx",
        $"构建{status}\n平台: {EditorUserBuildSettings.activeBuildTarget}\n时间: {System.DateTime.Now}");
}

// 自动化测试完成后通知
void OnTestFinished(int passed, int failed)
{
    var bot = new FeiShuBot("cli_xxx", "secret_xxx");
    bot.SendTextByPhone("13800138000",
        $"测试完成\n通过: {passed}\n失败: {failed}");
}
```

## API 参考

### `FeiShuBot(string appId, string appSecret)`

构造函数，传入飞书应用凭证。Token 会在首次调用 API 时自动获取。

### `bool SendText(string receiveIdType, string receiveId, string text)`

发送文本消息。

| 参数 | 说明 |
|------|------|
| `receiveIdType` | 接收者类型：`open_id` / `chat_id` / `user_id` / `email` |
| `receiveId` | 接收者 ID，格式需与类型匹配 |
| `text` | 消息文本内容 |

返回 `true` 表示发送成功。

### `bool SendTextByPhone(string mobile, string text)`

通过手机号发送文本消息。内部自动查询 open_id 再发送。

| 参数 | 说明 |
|------|------|
| `mobile` | 手机号，如 `"13800138000"` 或 `"+8613800138000"` |
| `text` | 消息文本内容 |

### `string GetOpenIdByPhone(string mobile)`

通过手机号查询用户的 open_id。中国大陆手机号无需加 `+86` 前缀。

### `string ListChats()`

获取机器人所在的群列表，返回原始 JSON 字符串。

### `bool RefreshToken()`

手动刷新 tenant_access_token。通常不需要调用，API 方法会自动处理。

## receive_id 获取方式

| 类型 | 格式示例 | 获取方式 |
|------|---------|---------|
| `chat_id` | `oc_xxxx` | 群设置中复制，或调用 `ListChats()` |
| `open_id` | `ou_xxxx` | 飞书管理后台通讯录，或调用 `ListChats()` 查看 `owner_id` |
| `email` | `user@company.com` | 用户的飞书注册邮箱 |

## 常见问题

### 发送失败 code=230001, ext=invalid receive_id

receive_id 格式不正确或不存在。请确认：
- `chat_id` 以 `oc_` 开头
- `open_id` 以 `ou_` 开头
- 使用的 ID 类型与 `receiveIdType` 参数匹配

### 发送失败 code=230002

机器人不在目标群中。需要先将机器人添加到群聊。

### 发送失败 code=230013

目标用户不在应用的可用范围内。前往飞书开放平台设置应用的可用范围。

### Token 获取失败

检查 App ID 和 App Secret 是否正确，应用是否已发布。

## 注意事项

- **安全提醒**：App Secret 是敏感信息，不要提交到公开仓库。正式使用建议将凭证移到 `.gitignore` 的配置文件中。
- **频率限制**：向同一用户/群发送消息限频 5 QPS。
- **同步调用**：当前实现为同步 HTTP 请求，适合编辑器场景。运行时高频调用建议改为异步。

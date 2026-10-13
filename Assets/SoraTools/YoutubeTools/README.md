# YoutubeTools - YouTube 视频下载工具

YoutubeTools 是一款集成在 Unity 中的 YouTube 视频/音频下载工具，提供现代化的图形界面，以 yt-dlp 为后端、ffmpeg 进行后期处理，支持从 144p 到 4K 的多种分辨率及多种音频格式下载。

---

## 功能概览

### 视频信息解析
- 解析 yt-dlp 输出的 JSON 元数据（无需第三方库）
- 提取视频 ID、标题、频道、上传者、时长、播放量、点赞数
- 获取缩略图 URL
- 解析上传日期（YYYYMMDD 格式）
- 枚举可用格式与画质列表

### 格式预设（9 种）

**视频格式**

| 预设 | 说明 |
|------|------|
| MP4 H.264 + AAC | 最佳兼容性，适合大多数播放器 |
| MP4 H.265 + AAC | 更高压缩率，文件更小 |
| WebM VP9 + Opus | 开源格式，适合网络播放 |
| MKV（最佳质量） | 保留最高视频/音频质量 |

**音频格式**

| 预设 | 说明 |
|------|------|
| MP3 320kbps | 高码率 MP3，广泛兼容 |
| MP3 192kbps | 标准码率 MP3 |
| M4A（AAC） | 苹果生态友好 |
| WAV（无损） | 无压缩原始音频 |
| FLAC | 无损压缩音频 |

### 画质选择（10 档）
Best（最佳）、4K（2160p）、2K（1440p）、1080p、720p、480p、360p、240p、144p、Worst（最低）

### 并发下载
- 支持多任务同时下载
- 实时进度显示：百分比、速度、预计剩余时间（ETA）
- 每个任务独立状态：下载中 / 合并中 / 完成 / 失败 / 已取消

### Cookie 支持
- 三种模式：无、从浏览器提取（Chrome / Firefox / Edge / Opera）、指定文件路径
- 用于访问年龄限制或私有内容

### 下载历史
- 持久化记录存储在 `%APPDATA%/YoutubeTools/history.json`
- 保留最近 100 条记录（自动裁剪）
- 显示：标题、文件路径、文件大小、下载时间

### 首次运行自动配置
- 检测到 yt-dlp.exe 缺失时自动从 GitHub 下载
- 检测到 ffmpeg 缺失时自动下载并解压
- 初始化期间显示进度遮罩层

---

## 主要文件结构

**核心层**

| 文件 | 说明 |
|------|------|
| `YtDlpManager.cs` | 进程执行、超时处理、从 GitHub 自动下载工具 |
| `YoutubeSettings.cs` | 单例设置管理，含工具路径解析 |

**数据模型**

| 文件 | 说明 |
|------|------|
| `VideoInfo.cs` | 视频元数据解析，含格式化显示方法 |
| `FormatInfo.cs` | 流格式详情（分辨率、编码、文件大小） |
| `DownloadTask.cs` | 下载任务状态（volatile 字段保证线程安全） |
| `DownloadPreset.cs` | 格式/画质枚举与 yt-dlp 参数构建 |

**服务层**

| 文件 | 说明 |
|------|------|
| `VideoInfoService.cs` | 轻量级 yt-dlp JSON 输出解析 |
| `ThumbnailService.cs` | 基于协程的缩略图下载（UnityWebRequest） |
| `DownloadService.cs` | 并发任务管理，正则解析下载进度 |
| `DownloadHistoryService.cs` | 线程安全的历史记录 CRUD（JSON） |

**UI 层**

| 文件 | 说明 |
|------|------|
| `YoutubeToolsApp.cs` | 主 MonoBehaviour，服务初始化与 UI 协调 |
| `UrlInputPanel.cs` | URL 输入框与获取触发 |
| `VideoPreviewPanel.cs` | 视频元数据展示（含缩略图） |
| `FormatSelectorPanel.cs` | 格式与画质预设选择 |
| `DownloadPanel.cs` | 下载任务列表，含每个任务的进度 UI |
| `SettingsPanel.cs` | 工具路径、下载目录、Cookie 模式配置 |

---

## 工作原理

### 线程模型
- `VideoInfoService.FetchInfo()` 在后台线程执行（60 秒超时）
- `DownloadService.StartDownload()` 为每个下载任务生成独立的 `Task.Run()`
- 进度正则解析在后台线程完成，volatile 字段供主线程 `Update()` 轮询
- 缩略图通过协程在主线程下载

### yt-dlp 集成
- 根据格式/画质预设动态构建命令参数
- 输出文件名模板：`%(title)s.%(ext)s`
- 优雅处理 yt-dlp 退出码 1（有警告但输出有效）
- 支持通过 `--ffmpeg-location` 指定 ffmpeg 路径

### 进度解析示例
yt-dlp 输出格式：
```
[download] 45.5% of ~1.2GB at 5.3MB/s ETA 01:30
```
通过正则提取：百分比、总大小、当前速度、ETA，并同步到 UI。

### 首次运行流程
1. 检测 yt-dlp / ffmpeg 是否存在
2. 不存在则从 GitHub Release 下载最新版
3. 解压 ZIP 到 `%APPDATA%/YoutubeTools/`
4. 显示带进度的遮罩层，完成后消失

---

## 配置参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| 窗口尺寸 | 1280×900 | 可配置 |
| 默认下载目录 | 用户自定义 | 存储于设置文件 |
| 格式预设 | 上次选择 | 持久化 |
| 画质预设 | 上次选择 | 持久化 |
| Cookie 模式 | 无 | None / Browser / File |
| 历史记录上限 | 100 条 | 超出后自动删除最旧记录 |

---

## 存储路径

| 内容 | 路径 |
|------|------|
| 用户设置 | `%APPDATA%/YoutubeTools/settings.json` |
| 下载历史 | `%APPDATA%/YoutubeTools/history.json` |
| yt-dlp | `%APPDATA%/YoutubeTools/yt-dlp.exe` |
| ffmpeg | `%APPDATA%/YoutubeTools/ffmpeg.exe` |
| ffprobe | `%APPDATA%/YoutubeTools/ffprobe.exe` |

---

## 依赖

- Unity 2022.3.56f1+
- yt-dlp（首次运行自动下载）
- ffmpeg（首次运行自动下载）
- .NET Standard 2.1 / C# 9.0
- Windows 平台

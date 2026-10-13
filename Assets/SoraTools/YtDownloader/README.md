# YtDownloader

基于 yt-dlp 的 Unity 视频下载工具，使用纯 IMGUI 构建 UI，兼容 Unity 2019.4 LTS ~ 2022.3 LTS，支持 Windows PC 独立打包。

---

## 目录

- [项目概述](#项目概述)
- [兼容性](#兼容性)
- [目录结构](#目录结构)
- [快速上手](#快速上手)
- [核心模块说明](#核心模块说明)
  - [YtDlSettings — 设置管理](#ytdlsettings--设置管理)
  - [YtDlManager — 进程管理与自动下载](#ytdlmanager--进程管理与自动下载)
  - [VideoInfoService — 视频信息获取](#videoinfoservice--视频信息获取)
  - [DownloadService — 下载任务管理](#downloadservice--下载任务管理)
  - [DownloadHistoryService — 历史记录](#downloadhistoryservice--历史记录)
  - [ThumbnailService — 缩略图下载](#thumbnailservice--缩略图下载)
  - [DownloadPreset — 格式与画质预设](#downloadpreset--格式与画质预设)
- [数据模型](#数据模型)
- [线程模型](#线程模型)
- [抽离下载功能到其他项目](#抽离下载功能到其他项目)
  - [最小依赖清单](#最小依赖清单)
  - [完整集成示例](#完整集成示例)
  - [仅获取视频信息](#仅获取视频信息)
  - [静默后台下载（无 UI）](#静默后台下载无-ui)
- [配置参考](#配置参考)
- [外部依赖](#外部依赖)
- [已知限制](#已知限制)

---

## 项目概述

YtDownloader 是一个 Unity Runtime 工具，封装了 [yt-dlp](https://github.com/yt-dlp/yt-dlp) 命令行程序，提供以下功能：

- 通过 URL 获取 YouTube / 主流视频平台的视频元数据
- 按格式（MP4/WebM/MKV/MP3/FLAC 等）和画质（4K～144p）下载视频或音频
- 支持多任务并发下载，带实时进度回调
- 下载完成后可自动将非 H.264 视频转码为 H.264（借助 ffmpeg）
- 持久化设置与下载历史（存储于 `%APPDATA%\YtDownloader\`）
- 支持 Cookie（浏览器提取 / 文件导入）以访问需登录的内容

---

## 兼容性

| 条件 | 要求 |
|------|------|
| Unity 版本 | 2019.4 LTS ~ 2022.3 LTS |
| C# 版本 | 6.0+（不使用 C# 8/9 特性） |
| .NET | Standard 2.0 / 2.1 |
| 平台 | **Windows PC 独立打包**（yt-dlp.exe / ffmpeg.exe 为 Windows x64 二进制）|
| UI 框架 | 纯 IMGUI（`OnGUI`），无 UIToolkit / TextMeshPro 依赖 |

> **注意**：Android / iOS 平台无法运行，因为 `System.Diagnostics.Process` 在移动端不可用，且 yt-dlp 为 Windows 可执行文件。

---

## 目录结构

```
Assets/SoraTools/YtDownloader/
├── Editor/
│   └── YtDownloaderSetup.cs          # 编辑器菜单：初始化场景 / 配置打包
├── Fonts/
│   └── ChineseFont.ttf               # 中文字体（由 Setup Scene 自动从系统导入）
├── Scripts/
│   ├── Core/                         # 纯 C# 业务逻辑，无 Unity UI 依赖
│   │   ├── YtDlSettings.cs           # 持久化设置单例
│   │   ├── YtDlManager.cs            # yt-dlp 进程调用 + 自动下载
│   │   ├── YtDlTask.cs               # 下载任务状态模型 + 历史记录模型
│   │   ├── YtDlVideoInfo.cs          # 视频元数据模型
│   │   └── YtDlFormatInfo.cs         # 格式/画质枚举与参数构建
│   ├── Services/                     # 业务服务层，可独立移植
│   │   ├── VideoInfoService.cs       # 获取视频元数据
│   │   ├── DownloadService.cs        # 并发下载管理
│   │   ├── DownloadHistoryService.cs # 历史记录 CRUD
│   │   └── ThumbnailService.cs       # 缩略图下载（需 MonoBehaviour）
│   └── UI/
│       ├── FolderBrowserHelper.cs    # Win32 文件夹选择对话框
│       └── YtDownloaderApp.cs        # 主 MonoBehaviour（IMGUI 全部 UI）
└── README.md
```

---

## 快速上手

### 在 Unity 编辑器中运行

1. 打开任意场景
2. 菜单 **SoraTools → YtDownloader - Setup Scene**
   - 自动创建 Camera（深色背景）
   - 挂载 `YtDownloaderApp` 组件
   - 导入系统中文字体（可选）
3. `Ctrl+S` 保存场景
4. 按 **Play** 运行
5. 首次运行时点击「自动下载」获取 yt-dlp 和 ffmpeg

### 打包 PC 独立版本

1. 菜单 **SoraTools → YtDownloader - Configure Build**（自动配置 PlayerSettings）
2. **File → Build Settings → Build**，选择 Windows Standalone (x86_64)

---

## 核心模块说明

### YtDlSettings — 设置管理

**文件**：`Scripts/Core/YtDlSettings.cs`
**命名空间**：`YtDownloader`

持久化单例，存储路径为 `%APPDATA%\YtDownloader\settings.json`。

```csharp
// 访问单例
YtDlSettings settings = YtDlSettings.Instance;

// 重要属性
string ytDlpExe  = settings.ResolvedYtDlpPath;   // yt-dlp.exe 完整路径
string ffmpegDir = settings.ResolvedFfmpegDir;    // ffmpeg 所在目录

bool ytDlpReady  = settings.YtDlpExists;          // yt-dlp.exe 是否存在
bool ffmpegReady = settings.FfmpegExists;         // ffmpeg.exe 是否存在

// 生成 Cookie 参数（供拼接 yt-dlp 命令行）
string cookieArgs = settings.BuildCookieArgs();
// 示例输出: "--cookies-from-browser chrome" 或 "--cookies \"C:\\path\\cookies.txt\""

// 保存设置
settings.DefaultDownloadPath = @"C:\Videos";
settings.Save();

// 重置单例（切换配置文件后调用）
YtDlSettings.ResetInstance();
```

**可配置字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `YtDlpPath` | string | 自定义 yt-dlp.exe 路径，留空使用默认 |
| `FfmpegDir` | string | 自定义 ffmpeg 目录，留空使用默认 |
| `DefaultDownloadPath` | string | 默认保存目录 |
| `FormatPresetIndex` | int | 视频格式枚举索引 |
| `QualityPresetIndex` | int | 画质枚举索引 |
| `CookieModeIndex` | int | Cookie 模式（0=无，1=浏览器，2=文件）|
| `BrowserChoiceIndex` | int | 浏览器选择（Chrome/Firefox/Edge/Opera）|
| `CookieFilePath` | string | Cookie 文件路径 |

---

### YtDlManager — 进程管理与自动下载

**文件**：`Scripts/Core/YtDlManager.cs`
**命名空间**：`YtDownloader`
**类型**：静态类，所有阻塞方法必须在**后台线程**调用

```csharp
// ── 执行 yt-dlp 命令（同步，阻塞，必须在后台线程） ──
ProcessResult result = YtDlManager.RunYtDlp(
    arguments: "--dump-json \"https://...\"",
    onStdout:  line => Debug.Log(line),   // 实时 stdout 回调（可选）
    onStderr:  line => Debug.LogWarning(line),
    timeoutMs: 60000
);

if (result.IsSuccess)
    Debug.Log(result.Output);   // JSON 或其他输出
else
    Debug.LogError(result.Error);

// ── 启动长时间运行的进程（如下载），返回 Process 供外部管理 ──
System.Diagnostics.Process proc = YtDlManager.StartYtDlpProcess("--version");
// 调用方负责 proc.Kill() 和 proc.Dispose()

// ── 自动下载 yt-dlp.exe（后台线程，带进度回调）──
var cts = new CancellationTokenSource();
bool ok = YtDlManager.DownloadYtDlp(
    onProgress: (float pct, string msg) => {
        // pct: 0.0 ~ 1.0，msg: 可读进度描述
        // 注意：此回调在后台线程执行，不要直接操作 Unity UI
    },
    ct: cts.Token
);

// ── 自动下载 ffmpeg.exe（从 BtbN GitHub Releases 获取 Win64 GPL 版）──
bool ok2 = YtDlManager.DownloadFfmpeg(onProgress, cts.Token);
```

**ProcessResult 字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `IsSuccess` | bool | yt-dlp 退出码为 0，或输出非空时为 true |
| `ExitCode` | int | 进程退出码 |
| `Output` | string | 完整 stdout 输出 |
| `Error` | string | 完整 stderr 输出 |

---

### VideoInfoService — 视频信息获取

**文件**：`Scripts/Services/VideoInfoService.cs`
**命名空间**：`YtDownloader`
**必须在后台线程调用**

```csharp
var service = new VideoInfoService();

// 同步获取视频信息（约 5~30 秒，取决于网络）
VideoInfoResult result = service.FetchInfo("https://www.youtube.com/watch?v=xxxx");

if (result.IsSuccess)
{
    VideoInfo v = result.Video;
    Debug.Log(v.Title);              // 视频标题
    Debug.Log(v.Channel);            // 频道名
    Debug.Log(v.DurationDisplay);    // "10:30" 或 "1:23:45"
    Debug.Log(v.ViewCountDisplay);   // "1.2M"
    Debug.Log(v.UploadDateDisplay);  // "2024-01-15"
    Debug.Log(v.ThumbnailUrl);       // 缩略图 URL
    Debug.Log(v.WebpageUrl);         // 原始页面 URL（用于下载）

    // 可用格式列表
    foreach (var fmt in v.Formats)
        Debug.Log($"{fmt.FormatId} {fmt.Ext} {fmt.Width}x{fmt.Height} {fmt.VCodec}");
}
else
{
    Debug.LogError(result.ErrorMessage);
}
```

**VideoInfoResult 字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `IsSuccess` | bool | 是否成功 |
| `ErrorMessage` | string | 失败原因 |
| `Video` | VideoInfo | 视频元数据对象 |

---

### DownloadService — 下载任务管理

**文件**：`Scripts/Services/DownloadService.cs`
**命名空间**：`YtDownloader`
**线程安全**，在 `Update()` 中轮询结果

这是最核心的服务，负责管理多个并发下载任务。

```csharp
var downloadSvc = new DownloadService();

// ── 启动一个下载任务 ──
string[] formatArgs = DownloadPreset.BuildArguments(
    VideoFormatPreset.Mp4H264,
    QualityPreset.P1080
);

DownloadTask task = downloadSvc.StartDownload(
    url:              video.WebpageUrl,    // 原始页面 URL
    videoTitle:       video.Title,         // 仅用于显示
    savePath:         @"C:\Videos",        // 保存目录（必须已存在）
    ytDlpFormatArgs:  formatArgs,          // 格式参数数组
    ensureH264:       true                 // true = 下载后若非 H.264 则转码
);

// task 立即返回，下载在后台线程进行

// ── 在 Update() 中轮询进度 ──
void Update()
{
    // 获取所有活跃任务的当前状态快照
    foreach (var t in downloadSvc.GetActiveTasks())
    {
        Debug.Log($"{t.VideoTitle}: {t.ProgressPercent:F1}% | {t.SpeedDisplay} | ETA {t.EtaDisplay}");
        Debug.Log($"状态: {t.Status} — {t.StatusMessage}");
    }

    // 取出已完成/失败/取消的任务（每帧调用直到返回 false）
    while (downloadSvc.TryDequeueCompleted(out DownloadTask completed))
    {
        if (completed.Status == DownloadStatus.Completed)
            Debug.Log("下载完成: " + completed.OutputPath);
        else if (completed.Status == DownloadStatus.Failed)
            Debug.LogError("下载失败: " + completed.StatusMessage);
    }
}

// ── 取消指定任务 ──
downloadSvc.CancelTask(task);

// ── 取消所有任务（在 OnDestroy 中调用）──
downloadSvc.CancelAll();
```

**DownloadTask 实时字段**（volatile，可在主线程安全读取）：

| 字段 | 类型 | 说明 |
|------|------|------|
| `TaskId` | int | 任务唯一 ID |
| `VideoTitle` | string | 视频标题 |
| `Status` | DownloadStatus | 当前状态（见下表）|
| `StatusMessage` | string | 可读状态描述 |
| `ProgressPercent` | float | 进度 0～100 |
| `SpeedBytesPerSec` | float | 下载速度（字节/秒）|
| `EtaSeconds` | int | 预估剩余秒数 |
| `OutputPath` | string | 最终文件路径（完成后更新）|
| `SpeedDisplay` | string | 格式化速度，如 "2.5 MB/s" |
| `EtaDisplay` | string | 格式化剩余时间，如 "1m30s" |
| `SizeDisplay` | string | 格式化大小，如 "45.2/320.0 MB" |

**DownloadStatus 枚举**：

| 值 | 含义 |
|----|------|
| `Idle` | 初始状态 |
| `Downloading` | 下载中 |
| `Merging` | 合并视频与音频流 |
| `Converting` | ffmpeg 转码 H.264 |
| `Completed` | 成功完成 |
| `Failed` | 下载失败 |
| `Cancelled` | 用户取消 |

---

### DownloadHistoryService — 历史记录

**文件**：`Scripts/Services/DownloadHistoryService.cs`
**命名空间**：`YtDownloader`
**存储位置**：`%APPDATA%\YtDownloader\history.json`（最多 100 条）

```csharp
var historySvc = new DownloadHistoryService();

// 读取所有历史（返回副本，线程安全）
List<DownloadHistoryEntry> all = historySvc.GetAll();

// 添加一条记录（通常在下载完成时调用）
historySvc.Add(new DownloadHistoryEntry
{
    Title       = task.VideoTitle,
    FilePath    = task.OutputPath,
    FileSize    = new FileInfo(task.OutputPath).Length,
    Url         = task.Url,
    CompletedAt = DateTime.Now.ToString("o"),   // ISO 8601
});

// 删除单条
historySvc.Remove(entry);

// 清空全部
historySvc.Clear();
```

**DownloadHistoryEntry 字段**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `Title` | string | 视频标题 |
| `FilePath` | string | 本地文件完整路径 |
| `FileSize` | long | 文件字节数 |
| `Url` | string | 原始视频 URL |
| `CompletedAt` | string | 完成时间（ISO 8601）|
| `FileSizeDisplay` | string | 格式化大小（只读）|

---

### ThumbnailService — 缩略图下载

**文件**：`Scripts/Services/ThumbnailService.cs`
**命名空间**：`YtDownloader`
**必须在主线程（MonoBehaviour）上使用**

```csharp
// 需要一个 MonoBehaviour 作为 Coroutine 宿主
var thumbSvc = new ThumbnailService(this);  // this = MonoBehaviour

thumbSvc.DownloadThumbnail(
    url:    video.ThumbnailUrl,
    onDone: (Texture2D tex) => {
        // 在主线程回调，tex 为 null 表示失败
        if (tex != null)
            myRawImage.texture = tex;
    }
);
```

---

### DownloadPreset — 格式与画质预设

**文件**：`Scripts/Core/YtDlFormatInfo.cs`
**命名空间**：`YtDownloader`

```csharp
// ── 构建 yt-dlp 格式参数 ──
string[] args = DownloadPreset.BuildArguments(
    VideoFormatPreset.Mp4H264,
    QualityPreset.P1080
);
// 输出示例:
// ["-f", "bestvideo[vcodec^=avc1][ext=mp4][height<=1080]+bestaudio[ext=m4a]/...",
//  "--merge-output-format", "mp4"]

// ── 枚举标签（用于 UI 显示）──
List<string> formatLabels  = DownloadPreset.FormatLabels;   // "MP4 (H.264+AAC)", ...
List<string> qualityLabels = DownloadPreset.QualityLabels;  // "最高画质", "1080p", ...

// ── 判断是否为纯音频格式 ──
bool isAudio = DownloadPreset.IsAudioOnly(VideoFormatPreset.Mp3_320); // true
```

**VideoFormatPreset 枚举**：

| 值 | 说明 |
|----|------|
| `Mp4H264` | MP4 容器，H.264 + AAC，最广泛兼容 |
| `Mp4H265` | MP4 容器，H.265 (AV1) + AAC |
| `WebmVP9` | WebM 容器，VP9 + Opus |
| `MkvBest` | MKV 容器，最佳质量无转码 |
| `Mp3_320` | MP3 320kbps（仅音频）|
| `Mp3_192` | MP3 192kbps（仅音频）|
| `M4A` | M4A AAC（仅音频）|
| `Wav` | WAV 无损（仅音频）|
| `Flac` | FLAC 无损（仅音频）|

**QualityPreset 枚举**：`Best / P2160 / P1440 / P1080 / P720 / P480 / P360 / P240 / P144 / Worst`

---

## 数据模型

### VideoInfo

```csharp
class VideoInfo {
    string Id;               // 视频唯一 ID
    string Title;            // 标题
    string Channel;          // 频道名
    long   Duration;         // 时长（秒）
    long   ViewCount;        // 播放量
    string UploadDate;       // "20240115" (yyyyMMdd)
    string ThumbnailUrl;     // 缩略图 URL
    string WebpageUrl;       // 原始页面 URL（传给下载服务）
    List<FormatInfo> Formats;// 所有可用格式

    // 格式化辅助属性（只读）
    string DurationDisplay;  // "10:30"
    string ViewCountDisplay; // "1.2M"
    string UploadDateDisplay;// "2024-01-15"
    bool   IsValid;          // Id 和 Title 均非空时为 true
}
```

---

## 线程模型

```
主线程 (Unity Update / OnGUI)
    │
    ├── 发起获取信息请求
    │       └── Task.Run() ──► 后台线程: VideoInfoService.FetchInfo()
    │                                  └── volatile _fetchReady = true
    │       Update() 检测 _fetchReady ──► 回到主线程处理结果
    │
    ├── 发起下载请求
    │       └── DownloadService.StartDownload() ──► Task.Run()
    │                  │
    │                  └── 后台线程: RunDownload()
    │                       ├── Process.Start(yt-dlp)
    │                       ├── volatile task.ProgressPercent = ...
    │                       └── lock(_completedLock): _completedQueue.Enqueue(task)
    │
    └── Update() 每帧:
            ├── GetActiveTasks()       → lock 保护的快照
            └── TryDequeueCompleted() → lock 保护的出队
```

**规则**：
- `VideoInfoService.FetchInfo()` 和 `YtDlManager.DownloadYtDlp/DownloadFfmpeg()` **只能在后台线程调用**
- `DownloadTask` 的 `volatile` 字段可在主线程安全读取
- `ThumbnailService.DownloadThumbnail()` **只能在主线程调用**（Coroutine）

---

## 抽离下载功能到其他项目

### 最小依赖清单

将以下文件复制到目标项目，保持相同的目录层级（或修改命名空间）：

```
必需文件（Core 层）：
  YtDlSettings.cs        ← 配置 + 路径解析
  YtDlManager.cs         ← yt-dlp 进程调用
  YtDlTask.cs            ← 任务状态模型
  YtDlFormatInfo.cs      ← 格式枚举 + 参数构建

必需文件（Service 层，按需选取）：
  VideoInfoService.cs    ← 需要获取视频信息时
  DownloadService.cs     ← 需要下载时
  DownloadHistoryService.cs ← 需要历史记录时
  ThumbnailService.cs    ← 需要显示缩略图时

可选（仅 Windows UI 辅助）：
  FolderBrowserHelper.cs ← Win32 文件夹对话框
```

**无需复制**：`YtDownloaderApp.cs`（UI 层，与业务逻辑完全解耦）、`YtDownloaderSetup.cs`（Editor 工具）

### 完整集成示例

以下示例演示如何在自己的 `MonoBehaviour` 中集成完整的下载流程：

```csharp
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using YtDownloader;   // 引用命名空间

public class MyVideoDownloader : MonoBehaviour
{
    private VideoInfoService       _videoInfoSvc;
    private DownloadService        _downloadSvc;
    private DownloadHistoryService _historySvc;

    // 后台线程写入，主线程读取（volatile 保证可见性）
    private volatile bool        _fetchReady;
    private VideoInfoResult      _fetchResult;

    void Awake()
    {
        _videoInfoSvc = new VideoInfoService();
        _downloadSvc  = new DownloadService();
        _historySvc   = new DownloadHistoryService();
    }

    void OnDestroy()
    {
        _downloadSvc.CancelAll();   // 应用退出时取消所有下载
    }

    // ── 第一步：检查 yt-dlp 是否就绪 ──
    public bool IsReady()
    {
        return YtDlSettings.Instance.YtDlpExists &&
               YtDlSettings.Instance.FfmpegExists;
    }

    // ── 第二步：获取视频信息（异步发起，Update 中接收结果）──
    public void FetchVideoInfo(string url)
    {
        _fetchReady = false;
        System.Threading.Tasks.Task.Run(() =>
        {
            _fetchResult = _videoInfoSvc.FetchInfo(url);
            _fetchReady  = true;   // volatile 写，主线程可见
        });
    }

    void Update()
    {
        // 接收获取信息的结果
        if (_fetchReady)
        {
            _fetchReady = false;
            OnVideoInfoReceived(_fetchResult);
        }

        // 轮询下载进度（每帧刷新 UI）
        foreach (var task in _downloadSvc.GetActiveTasks())
        {
            UpdateProgressUI(task);
        }

        // 处理完成的任务
        while (_downloadSvc.TryDequeueCompleted(out var completed))
        {
            HandleCompletedTask(completed);
        }
    }

    private void OnVideoInfoReceived(VideoInfoResult result)
    {
        if (!result.IsSuccess)
        {
            Debug.LogError("获取失败: " + result.ErrorMessage);
            return;
        }

        VideoInfo v = result.Video;
        Debug.Log($"标题: {v.Title}");
        Debug.Log($"时长: {v.DurationDisplay}  播放: {v.ViewCountDisplay}");

        // 获取成功后开始下载
        StartDownload(v, @"C:\Videos", VideoFormatPreset.Mp4H264, QualityPreset.P1080);
    }

    // ── 第三步：启动下载 ──
    public DownloadTask StartDownload(VideoInfo video, string saveDir,
        VideoFormatPreset fmt, QualityPreset quality)
    {
        if (!Directory.Exists(saveDir))
            Directory.CreateDirectory(saveDir);

        string[] fmtArgs   = DownloadPreset.BuildArguments(fmt, quality);
        bool     ensure264 = (fmt == VideoFormatPreset.Mp4H264);

        return _downloadSvc.StartDownload(
            url:             video.WebpageUrl,
            videoTitle:      video.Title,
            savePath:        saveDir,
            ytDlpFormatArgs: fmtArgs,
            ensureH264:      ensure264
        );
    }

    // ── 取消任务 ──
    public void Cancel(DownloadTask task) => _downloadSvc.CancelTask(task);

    // ── 实现这两个方法连接你自己的 UI ──
    private void UpdateProgressUI(DownloadTask task)
    {
        // 示例：打印到控制台，替换为你的 UI 更新逻辑
        Debug.Log($"[{task.TaskId}] {task.VideoTitle}: " +
                  $"{task.ProgressPercent:F1}% | {task.SpeedDisplay} | ETA {task.EtaDisplay}");
    }

    private void HandleCompletedTask(DownloadTask task)
    {
        if (task.Status == DownloadStatus.Completed)
        {
            Debug.Log("下载完成: " + task.OutputPath);

            // 写入历史记录
            long size = File.Exists(task.OutputPath)
                ? new FileInfo(task.OutputPath).Length : 0;
            _historySvc.Add(new DownloadHistoryEntry
            {
                Title       = task.VideoTitle,
                FilePath    = task.OutputPath,
                FileSize    = size,
                Url         = task.Url,
                CompletedAt = System.DateTime.Now.ToString("o"),
            });
        }
        else
        {
            Debug.LogWarning($"任务结束 ({task.Status}): {task.StatusMessage}");
        }
    }
}
```

---

### 仅获取视频信息

如果只需要解析视频元数据（标题、时长、缩略图等），不需要下载功能：

**最小文件**：`YtDlSettings.cs` + `YtDlManager.cs` + `YtDlVideoInfo.cs` + `YtDlFormatInfo.cs` + `VideoInfoService.cs`

```csharp
// 在任意 MonoBehaviour 中
private volatile bool       _ready;
private VideoInfoResult     _result;

public void GetInfo(string url)
{
    System.Threading.Tasks.Task.Run(() =>
    {
        _result = new VideoInfoService().FetchInfo(url);
        _ready  = true;
    });
}

void Update()
{
    if (!_ready) return;
    _ready = false;

    if (_result.IsSuccess)
    {
        var v = _result.Video;
        // 使用 v.Title, v.ThumbnailUrl, v.WebpageUrl 等
    }
}
```

---

### 静默后台下载（无 UI）

如果只需要触发下载、不需要任何 UI 或历史记录：

**最小文件**：`YtDlSettings.cs` + `YtDlManager.cs` + `YtDlTask.cs` + `YtDlFormatInfo.cs` + `DownloadService.cs`

```csharp
public class SilentDownloader : MonoBehaviour
{
    private DownloadService _svc = new DownloadService();

    void OnDestroy() => _svc.CancelAll();

    // 一行发起下载
    public DownloadTask Download(string webpageUrl, string title, string saveDir)
    {
        string[] args = DownloadPreset.BuildArguments(
            VideoFormatPreset.Mp4H264, QualityPreset.Best);
        return _svc.StartDownload(webpageUrl, title, saveDir, args);
    }

    void Update()
    {
        while (_svc.TryDequeueCompleted(out var task))
        {
            if (task.Status == DownloadStatus.Completed)
                Debug.Log("完成: " + task.OutputPath);
        }
    }
}
```

---

## 配置参考

### 修改默认数据目录

`YtDlSettings` 默认将数据存储在 `%APPDATA%\YtDownloader\`。
如需修改，打开 `YtDlSettings.cs`，调整此处：

```csharp
private static readonly string AppDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "YtDownloader"   // ← 修改此字符串
);
```

### 使用自定义 yt-dlp / ffmpeg 路径

```csharp
var s = YtDlSettings.Instance;
s.YtDlpPath = @"D:\tools\yt-dlp.exe";   // 绝对路径
s.FfmpegDir = @"D:\tools\ffmpeg\bin\";   // ffmpeg.exe 所在目录
s.Save();
```

---

## 外部依赖

| 依赖 | 来源 | 用途 | 获取方式 |
|------|------|------|---------|
| **yt-dlp.exe** | [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) | 视频信息解析 + 下载 | 首次运行时自动下载到 `%APPDATA%\YtDownloader\` |
| **ffmpeg.exe** | [BtbN/ffmpeg-builds](https://github.com/BtbN/ffmpeg-builds/releases) | 视频音频流合并 + H.264 转码 | 首次运行时自动下载 |
| **ffprobe.exe** | 同上（同一 ZIP 包内）| 探测视频编码格式 | 随 ffmpeg 自动获取 |

所有二进制文件仅在 **Windows x64** 运行，不会打包进 Unity 项目，运行时按需下载。

---

## 已知限制

- 仅支持 **Windows PC** 平台（yt-dlp / ffmpeg 为 Windows 可执行文件）
- 需要**网络连接**才能下载视频和自动获取工具
- yt-dlp 支持的平台取决于其版本，YouTube 的反爬策略变化可能影响可用性
- 部分视频需要登录 Cookie 才能下载（通过设置面板配置）
- ffmpeg 约 120 MB，初次下载耗时较长，建议在加载画面中完成

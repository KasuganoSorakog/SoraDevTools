# ADBCommandTools - Android 设备调试工具

ADBCommandTools 是一款集成在 Unity 编辑器中的 Android 设备管理工具，提供图形化界面操作 ADB（Android Debug Bridge），让开发者无需手动输入命令行即可完成常见的 Android 调试任务。

---

## 功能概览

### 设备管理
- 自动列出已连接的 Android 设备
- 支持通过 IP 地址进行 TCP/IP 无线连接
- 支持配对码连接（Android 11+）
- 局域网设备扫描

### APK / OBB 安装
- 拖拽安装 APK 文件
- 支持 OBB 扩展包安装
- 多设备批量安装
- 安装进度实时显示

### Logcat 日志查看器
- 实时流式输出设备日志
- 自定义关键词过滤
- 自动滚动 + 日志缓冲区管理（80KB，20块×4KB）
- 150ms 刷新节流，保证 UI 流畅

### 应用管理
- 列出设备上已安装的应用
- 按包名过滤查找
- 交互式应用选择

### 文件管理器
- 浏览设备文件系统
- 推送/拉取文件（PC ↔ 设备）
- 创建远程目录
- 删除远程文件

### 性能监控
- 基于 Logcat 的性能数据分析
- 指定包名的内存使用分析
- 系统指标采集

### 屏幕镜像
- 将 Android 设备屏幕投射到开发机

---

## 主要文件结构

| 文件 | 说明 |
|------|------|
| `AdbToolsApp.cs` | 主应用控制器，负责 UI 构建与状态管理 |
| `AdbService.cs` | 核心 ADB 命令执行服务，支持同步/异步调用 |
| `CommandHistory.cs` | 命令历史记录持久化（JSON 格式） |
| `WebView2Interop.cs` | Windows WebView2 COM 互操作，用于内嵌网页内容 |
| `MainThreadDispatcher.cs` | 线程安全调度器，将后台线程结果派发到主线程 |
| `FileDialogHelper.cs` | 跨平台文件选择对话框（Win32 / Editor） |
| `AdbToolsSetup.cs` | 编辑器菜单注册与场景配置 |

---

## 工作原理

### 服务架构
`AdbService` 通过 `System.Diagnostics.Process` 执行 ADB 命令，命令超时时间默认为 60 秒，`install` 等重量级操作延长至 180 秒。

### 线程模型
- 同步命令在后台线程中执行
- Logcat 流式输出作为持久化后台进程运行
- `MainThreadDispatcher` 将结果安全地派发回主线程进行 UI 更新

### 历史记录
命令历史和路径记录存储在：
```
{Application.persistentDataPath}/adb_command_history.json
```

### Windows 集成
- 使用 Win32 API（comdlg32.dll）实现文件对话框
- 支持嵌入 WebView2 浏览器组件
- 支持从资源管理器拖拽文件

---

## 配置参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| 窗口尺寸 | 1280×800 | 可通过 SerializeField 调整 |
| Logcat 刷新间隔 | 150ms | UI 节流时间 |
| 命令超时 | 60s（install 为 180s） | ADB 命令最大等待时间 |
| 字符编码 | UTF-8 | 进程输入/输出流编码 |
| ADB 路径 | 系统环境变量中的 ADB | 按 `ANDROID_SDK_ROOT`、`ANDROID_HOME`、`Path` 顺序检测，未找到时回退到 `StreamingAssets/ApkTools/adb.exe` |

---

## 依赖

- Unity 2022.3.56f1+
- ADB 工具链（优先使用系统环境变量，包内 `StreamingAssets/ApkTools/adb.exe` 作为回退）
- Windows 平台（Win32 API 集成）
- .NET Standard 2.1 / C# 9.0

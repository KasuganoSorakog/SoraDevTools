# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

SoraDevTools is a Unity Editor Extension project (Unity 2022.3.56f1, C# 9.0, .NET Standard 2.1) containing three main components:

- **MagicBox**: A resource library management tool with SQLite-backed storage, supporting import/search/tagging of Unity assets (packages, scripts, textures, models, etc.)
- **MCPForUnity**: Model Context Protocol integration plugin for AI-powered code assistance in the Unity Editor
- **ADBCommandTools**: Android Device Bridge utilities for Android development workflows

## Build & Development

This is a Unity project — there are no CLI build scripts. All compilation is handled by Unity's C# pipeline.

- **Solution file**: `SoraDevTools.sln` (7 C# projects)
- **IDE**: VS Code with "Visual Studio Tools for Unity" extension (`vstuc`), debug via "Attach to Unity" launch config
- **No test suite implemented** (test framework package is included but no tests written yet)
- **No linter/formatter config** — relies on Unity's default C# compiler at Warning Level 4

## Architecture

MagicBox follows a layered architecture:

```
UI Layer (UI Toolkit: UXML/USS)
    ↓
Service Layer (LibraryManager, ImportService, SearchService, TagService, FolderService)
    ↓
Data Layer (LibraryDatabase → SQLiteConnection)
```

### Key Patterns

- **Singleton services**: LibraryManager, AppSettings, LibraryDatabase are singleton instances initialized by MagicBoxApp
- **UI Toolkit**: Uses modern UIElements (UXML/USS) instead of legacy IMGUI. Styles in `Assets/MagicBox/UI/Styles/`
- **Win32 interop**: Native Windows API calls for file dialogs (`Win32Dialog`) and drag-drop from Explorer (`Win32DragDrop`)
- **Settings persistence**: `%APPDATA%/MagicBox/settings.json` via AppSettings
- **Localization**: `Loc.cs` provides i18n support

### Namespaces

- `MagicBox.Core` — Database, settings, SQLite, localization
- `MagicBox.Models` — Data models (ResourceItem, ResourceFolder, ResourceTag, ResourceLibrary, ResourceType)
- `MagicBox.Services` — Business logic services
- `MagicBox.UI` — Views and UI components

### Database Schema (SQLite)

4 tables: `resources`, `folders`, `tags`, `resource_tags`. Indexes on `resource_type`, `folder_id`, `rating`, `imported_at`. Schema auto-created on first use by LibraryDatabase.

### Resource Types

15 types defined in `ResourceType` enum: UnityPackage, ZipArchive, CSharpScript, ShaderFile, Prefab, Material, Texture, Model3D, Audio, Animation, Font, Document, Executable, Folder, Other. Auto-detected from file extension via `ResourceItem.DetectType()`.

## Key Entry Points

- `Assets/MagicBox/Scripts/UI/MagicBoxApp.cs` — Main application entry, initializes all services, manages UI state
- `Assets/MagicBox/Scripts/Core/LibraryDatabase.cs` — SQLite CRUD operations and schema management
- `Assets/MagicBox/Scripts/Services/LibraryManager.cs` — Library lifecycle (create/open/close)
- `Assets/MagicBox/Editor/MagicBoxSetup.cs` — Editor initialization
- `Packages/MCPForUnity/` — MCP plugin (separate package with Editor/Runtime split)

## MCP Integration

MCPForUnity is configured as a Unity package under `Packages/MCPForUnity/`. It includes configurators for multiple AI clients (Codex, Codex Desktop, Cursor, VS Code, Windsurf, Rider, etc.). Allowed MCP tools are defined in `.Codex/settings.local.json`.

## Reference Documentation

Detailed development spec with UI layouts, database schema, and phased development plan: `Documents/DevPlan_ResourceManager.md`

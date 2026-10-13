using System;
using System.Collections.Generic;

namespace MagicBox.Models
{
    [Serializable]
    public class ResourceItem
    {
        public string Id;
        public string Name;
        public string FileName;
        public string RelativePath;
        public string OriginalPath;
        public ResourceType Type;
        public long FileSize;
        public string FileHash;
        public int Rating;
        public string Version;
        public string Description;
        public string SourceUrl;
        public string UnityVersionMin;
        public string UnityVersionMax;
        public string ThumbnailPath;
        public bool IsReference;
        public string ImportedAt;
        public string FileModifiedAt;
        public string FolderId;
        public List<string> TagIds = new List<string>();

        public string FileSizeDisplay
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024.0 * 1024.0):F1} MB";
                return $"{FileSize / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        public static ResourceType DetectType(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return ResourceType.Other;
            extension = extension.ToLowerInvariant().TrimStart('.');
            switch (extension)
            {
                case "unitypackage": return ResourceType.UnityPackage;
                case "zip": case "rar": case "7z": case "gz": case "tar": return ResourceType.ZipArchive;
                case "cs": return ResourceType.CSharpScript;
                case "shader": case "cginc": case "hlsl": case "glsl": case "compute": return ResourceType.ShaderFile;
                case "prefab": return ResourceType.Prefab;
                case "mat": return ResourceType.Material;
                case "png": case "jpg": case "jpeg": case "psd": case "tga": case "bmp": case "gif": case "tiff": case "svg": return ResourceType.Texture;
                case "fbx": case "obj": case "blend": case "dae": case "3ds": case "max": return ResourceType.Model3D;
                case "wav": case "mp3": case "ogg": case "aiff": case "flac": return ResourceType.Audio;
                case "anim": case "controller": case "overridecontroller": return ResourceType.Animation;
                case "ttf": case "otf": case "fon": case "woff": return ResourceType.Font;
                case "pdf": case "md": case "txt": case "doc": case "docx": case "drawio": case "csv": return ResourceType.Document;
                case "exe": case "msi": case "bat": case "cmd": case "sh": return ResourceType.Executable;
                default: return ResourceType.Other;
            }
        }
    }
}

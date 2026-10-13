using System;

namespace MagicBox.Models
{
    [Serializable]
    public class ResourceLibrary
    {
        public string Id;
        public string Name;
        public string RootPath;
        public string DatabasePath;
        public string CreatedAt;
        public string UpdatedAt;

        public ResourceLibrary() { }

        public ResourceLibrary(string name, string rootPath)
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
            RootPath = rootPath;
            DatabasePath = System.IO.Path.Combine(rootPath, "magicbox.db");
            CreatedAt = DateTime.Now.ToString("o");
            UpdatedAt = CreatedAt;
        }
    }
}

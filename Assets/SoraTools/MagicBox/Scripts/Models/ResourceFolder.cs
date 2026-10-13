using System;

namespace MagicBox.Models
{
    [Serializable]
    public class ResourceFolder
    {
        public string Id;
        public string Name;
        public string ParentId;
        public int SortOrder;
        public string ColorTag;
        public bool IsSmartFolder;
        public string SmartFilter;
        public string CreatedAt;

        public ResourceFolder() { }

        public ResourceFolder(string name, string parentId = null)
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
            ParentId = parentId;
            SortOrder = 0;
            ColorTag = "";
            IsSmartFolder = false;
            SmartFilter = "";
            CreatedAt = DateTime.Now.ToString("o");
        }
    }
}

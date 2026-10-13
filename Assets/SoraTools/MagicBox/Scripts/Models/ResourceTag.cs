using System;

namespace MagicBox.Models
{
    [Serializable]
    public class ResourceTag
    {
        public string Id;
        public string Name;
        public string GroupName;
        public string Color;
        public int UsageCount;

        public ResourceTag() { }

        public ResourceTag(string name, string groupName = "", string color = "#4FC3F7")
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
            GroupName = groupName;
            Color = color;
            UsageCount = 0;
        }
    }
}

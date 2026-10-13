using System.Collections.Generic;
using System.Linq;
using MagicBox.Models;

namespace MagicBox.Services
{
    public class TagService
    {
        private readonly LibraryManager _lib;
        public TagService(LibraryManager lib) { _lib = lib; }

        public ResourceTag CreateTag(string name, string group = "", string color = "#4FC3F7")
        {
            if (!_lib.IsLibraryOpen) return null;
            var existing = GetAllTags().Find(t => t.Name == name);
            if (existing != null) return existing;
            var tag = new ResourceTag(name, group, color);
            _lib.Database.InsertTag(tag);
            return tag;
        }

        public void UpdateTag(ResourceTag tag) { if (_lib.IsLibraryOpen) _lib.Database.UpdateTag(tag); }
        public void DeleteTag(string id) { if (_lib.IsLibraryOpen) _lib.Database.DeleteTag(id); }
        public List<ResourceTag> GetAllTags() => _lib.IsLibraryOpen ? _lib.Database.GetAllTags() : new List<ResourceTag>();

        public Dictionary<string, List<ResourceTag>> GetTagsByGroup()
        {
            var groups = new Dictionary<string, List<ResourceTag>>();
            foreach (var t in GetAllTags())
            {
                string g = string.IsNullOrEmpty(t.GroupName) ? "General" : t.GroupName;
                if (!groups.ContainsKey(g)) groups[g] = new List<ResourceTag>();
                groups[g].Add(t);
            }
            return groups;
        }

        public void AddTagToResources(List<string> rids, string tid)
        {
            if (!_lib.IsLibraryOpen) return;
            foreach (var r in rids) _lib.Database.AddTagToResourceNoCount(r, tid);
            _lib.Database.RefreshTagCount(tid);
        }

        public void RemoveTagFromResources(List<string> rids, string tid)
        {
            if (!_lib.IsLibraryOpen) return;
            foreach (var r in rids) _lib.Database.RemoveTagFromResourceNoCount(r, tid);
            _lib.Database.RefreshTagCount(tid);
        }
        public List<ResourceTag> GetTagsForResource(string rid)
        {
            if (!_lib.IsLibraryOpen) return new List<ResourceTag>();
            var tagIds = new HashSet<string>(_lib.Database.GetResourceTagIds(rid));
            return GetAllTags().Where(t => tagIds.Contains(t.Id)).ToList();
        }
    }
}

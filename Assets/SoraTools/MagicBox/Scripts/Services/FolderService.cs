using System.Collections.Generic;
using MagicBox.Models;

namespace MagicBox.Services
{
    public class FolderService
    {
        private readonly LibraryManager _lib;
        public FolderService(LibraryManager lib) { _lib = lib; }

        public ResourceFolder CreateFolder(string name, string parentId = null)
        {
            if (!_lib.IsLibraryOpen) return null;
            var f = new ResourceFolder(name, parentId);
            f.SortOrder = _lib.Database.GetChildFolders(parentId).Count;
            _lib.Database.InsertFolder(f);
            return f;
        }

        public void RenameFolder(string id, string newName)
        {
            var f = _lib.Database.GetFolder(id);
            if (f == null) return;
            f.Name = newName;
            _lib.Database.UpdateFolder(f);
        }

        public void SetFolderColor(string id, string color)
        {
            var f = _lib.Database.GetFolder(id);
            if (f == null) return;
            f.ColorTag = color;
            _lib.Database.UpdateFolder(f);
        }

        public void DeleteFolder(string id) { if (_lib.IsLibraryOpen) _lib.Database.DeleteFolder(id); }
        public List<ResourceFolder> GetRootFolders() => _lib.IsLibraryOpen ? _lib.Database.GetChildFolders(null) : new List<ResourceFolder>();
        public List<ResourceFolder> GetChildFolders(string pid) => _lib.IsLibraryOpen ? _lib.Database.GetChildFolders(pid) : new List<ResourceFolder>();
        public List<ResourceFolder> GetAllFolders() => _lib.IsLibraryOpen ? _lib.Database.GetAllFolders() : new List<ResourceFolder>();

        public void MoveResourcesToFolder(List<string> rids, string fid)
        {
            if (!_lib.IsLibraryOpen) return;
            _lib.Database.MoveResourcesToFolder(rids, fid);
        }
    }
}

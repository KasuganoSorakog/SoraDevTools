using System.Collections.Generic;
using MagicBox.Models;

namespace MagicBox.Services
{
    public class SearchService
    {
        private readonly LibraryManager _lib;
        public SearchService(LibraryManager lib) { _lib = lib; }

        public List<ResourceItem> Search(SearchQuery q) =>
            _lib.IsLibraryOpen ? _lib.Database.SearchResources(q) : new List<ResourceItem>();

        public List<ResourceItem> QuickSearch(string keyword) =>
            Search(new SearchQuery { Keyword = keyword, Limit = 10000 });

        public List<ResourceItem> GetByType(ResourceType type) =>
            Search(new SearchQuery { TypeFilter = type, Limit = 10000 });

        public List<ResourceItem> GetByTag(string tagId) =>
            Search(new SearchQuery { TagIds = new List<string> { tagId }, Limit = 10000 });

        public List<ResourceItem> GetByFolder(string folderId) =>
            _lib.IsLibraryOpen ? _lib.Database.GetResourcesByFolder(folderId) : new List<ResourceItem>();

        public List<ResourceItem> GetAll() =>
            _lib.IsLibraryOpen ? _lib.Database.GetAllResources() : new List<ResourceItem>();
    }
}

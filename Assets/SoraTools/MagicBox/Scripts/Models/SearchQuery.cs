using System;
using System.Collections.Generic;

namespace MagicBox.Models
{
    [Serializable]
    public class SearchQuery
    {
        public string Keyword = "";
        public ResourceType? TypeFilter = null;
        public List<string> TagIds = new List<string>();
        public string FolderId = null;
        public int? MinRating = null;
        public long? MinSize = null;
        public long? MaxSize = null;
        public string SortField = "imported_at";
        public bool SortDescending = true;
        public int Offset = 0;
        public int Limit = 100;
    }
}

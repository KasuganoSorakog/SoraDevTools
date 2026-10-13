namespace MagicBox.Services
{
    /// <summary>
    /// Composition root that groups all MagicBox services.
    /// Created once by MagicBoxApp and passed to ViewModel and Views.
    /// </summary>
    public class ServiceContainer
    {
        public LibraryManager Library { get; }
        public ImportService Import { get; }
        public SearchService Search { get; }
        public TagService Tags { get; }
        public FolderService Folders { get; }

        public ServiceContainer()
        {
            Library = LibraryManager.Instance;
            Import = new ImportService(Library);
            Search = new SearchService(Library);
            Tags = new TagService(Library);
            Folders = new FolderService(Library);
        }
    }
}

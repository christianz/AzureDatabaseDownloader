namespace ExportDatabaseFromAzure
{
    internal abstract class ProjectProfile
    {
        public abstract string Name { get; }
        public abstract string AzureConnectionString { get; }
        public abstract string[] DatabasesToDownload { get; }
        public abstract string LocalDbUser { get; }
        public abstract bool IsActive { get; }
    }
}

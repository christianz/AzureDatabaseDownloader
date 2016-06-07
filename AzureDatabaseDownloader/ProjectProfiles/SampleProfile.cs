namespace AzureDatabaseDownloader.ProjectProfiles
{
    internal class SampleProfile : ProjectProfile
    {
        /// <summary>
        /// The project name. This is also used for determining the local folder to export the .bacpac files to,
        /// and the display name in the list of selectable projects.
        /// </summary>
        public override string Name => "Test";

        /// <summary>
        /// Connection string to your Azure db server.
        /// </summary>
        public override string AzureConnectionString => "server=tcp:test.database.windows.net,1433;uid=user_id;pwd=your_password;";

        /// <summary>
        /// A list of databases to download. Each database will be downloaded to your local folder with a timestamp,
        /// and then imported to your server of choice (app.config -> LocalConnectionString).
        /// </summary>
        public override string[] DatabasesToDownload => new[]
        {
            "MyTest1",
            "MyTest2"
        };

        /// <summary>
        /// Adds db_owner permissions to this user for each downloaded database. If set to null, this step is skipped.
        /// </summary>
        public override string LocalDbUser => "iusr_Sample";

        /// <summary>
        /// If set to false, won't show up in the profile selection list
        /// </summary>
        public override bool IsActive => true;
    }
}

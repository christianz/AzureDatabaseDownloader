namespace AzureDatabaseDownloader.ProjectProfiles
{
    internal class SampleProfile : ProjectProfile
    {
        public override string Name => "Test";
        public override string AzureConnectionString => "server=tcp:test.database.windows.net,1433;uid=user_id;pwd=your_password;";

        public override string[] DatabasesToDownload => new[]
        {
            "MyTest1",
            "MyTest2"
        };

        public override string LocalDbUser => "iusr_Sample";
        public override bool IsActive => true;
    }
}

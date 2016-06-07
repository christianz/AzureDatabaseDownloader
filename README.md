# Azure Database Downloader

Export .bacpac files from Azure databases and imports them to another server (usually localhost). Useful for fetching copies of Azure databases for testing purposes.

Supports exporting/importing several databases at a time. Keeps backups of the databases on your local file system.

## Usage

#### Change the values in app.config

```xml
  <connectionStrings>
    <add name="LocalConnectionString" connectionString="server=localhost;database=master;trusted_connection=true;"/>
  </connectionStrings>
  <appSettings>
    <add key="LocalDbFolder" value="C:\Temp" />
  </appSettings>
```

#### Add a database project profile to the ProjectProfiles/ folder with the following format:

```csharp
namespace ExportDatabaseFromAzure.ProjectProfiles
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
```

#### Press F5

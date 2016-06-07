using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.SqlServer.Dac;

namespace AzureDatabaseDownloader
{
    class Program
    {
        private static readonly string DestinationConnectionString = ConfigurationManager.ConnectionStrings["LocalConnectionString"].ConnectionString;
        private static readonly string LocalDbContainer = ConfigurationManager.AppSettings["LocalDbFolder"];

        private static List<ProjectProfile> _profiles;

        static void Main(string[] args)
        {
            _profiles = LoadProjectProfiles().Where(p => p.IsActive).ToList();

            Console.WriteLine("--- WARNING ---");
            Console.WriteLine("Local databases for the selected profile will be overwritten! Ctrl+C out NOW if you'd like to keep them!");
            Console.WriteLine();

            Console.WriteLine("Select project profile to run:");
            var i = 1;
            int selectedIdx;

            foreach (var p in _profiles)
            {
                Console.WriteLine($"[{i++}] {p.Name}");
            }

            Console.WriteLine($"[{i}] Exit");

            var k = Console.ReadKey();

            if (!int.TryParse(k.KeyChar.ToString(), out selectedIdx))
            {
                return;
            }

            if (_profiles.Count < selectedIdx)
            {
                return;
            }

            var selectedProfile = _profiles[selectedIdx - 1];

            Console.WriteLine();
            Console.WriteLine($"Syncing {selectedProfile.Name}...");

            var azureConnectionString = selectedProfile.AzureConnectionString;

            foreach (var db in selectedProfile.DatabasesToDownload)
            {
                Console.WriteLine($"Fetching {db}...");
                Console.WriteLine();

                var localFilePath = Path.Combine(LocalDbContainer, selectedProfile.Name);

                if (!Directory.Exists(localFilePath))
                {
                    Directory.CreateDirectory(localFilePath);
                }

                localFilePath = Path.Combine(localFilePath, $"{db}_{DateTime.Now.ToString("dd_MM_yyyy_HH_mm")}.bacpac");

                var dac = new DacServices(azureConnectionString);

                dac.ProgressChanged += (sender, eventArgs) =>
                {
                    Console.WriteLine($"[{db}] {eventArgs.Message}");
                };

                dac.ExportBacpac(localFilePath, db, DacSchemaModelStorageType.File);

                Console.WriteLine($"[{db}] Export completed");

                var pk = BacPackage.Load(localFilePath);
                var spec = new DacAzureDatabaseSpecification
                {
                    Edition = DacAzureEdition.Basic
                };

                using (var sqlConn = new SqlConnection(DestinationConnectionString))
                using (var singleUserCmd = new SqlCommand(string.Format("IF db_id('{0}') is not null ALTER DATABASE [{0}] SET  SINGLE_USER WITH ROLLBACK IMMEDIATE", db), sqlConn))
                using (var dropCmd = new SqlCommand(string.Format("IF db_id('{0}') is not null DROP DATABASE [{0}]", db), sqlConn))
                {
                    sqlConn.Open();

                    singleUserCmd.ExecuteNonQuery();
                    dropCmd.ExecuteNonQuery();
                }

                var local = new DacServices(DestinationConnectionString);
                local.ProgressChanged += (sender, eventArgs) =>
                {
                    Console.WriteLine($"[{db}] {eventArgs.Message}");
                };

                local.ImportBacpac(pk, db, spec);

                if (!string.IsNullOrEmpty(selectedProfile.LocalDbUser))
                {
                    using (var sqlConn = new SqlConnection(DestinationConnectionString))
                    using (var loginCmd = new SqlCommand(string.Format("CREATE USER [{0}] FOR LOGIN [{0}]; ALTER ROLE [db_owner] ADD MEMBER [{0}]", selectedProfile.LocalDbUser, db), sqlConn))
                    {
                        sqlConn.Open();

                        try
                        {
                            loginCmd.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"WARNING: Couldn't add user {selectedProfile.LocalDbUser} because: {ex.Message}");
                        }
                    }
                }

                Console.Write("done.");
                Console.WriteLine();
            }
        }

        private static IEnumerable<ProjectProfile> LoadProjectProfiles()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var types = assembly.GetTypes().Where(t => t.Namespace.Contains("ProjectProfiles") && t.IsSubclassOf(typeof(ProjectProfile)));

            return types.Select(t => (ProjectProfile)Activator.CreateInstance(t));
        }
    }
}

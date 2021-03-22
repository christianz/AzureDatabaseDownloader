using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using CommandLine;
using Microsoft.SqlServer.Dac;

namespace AzureDatabaseDownloader
{
    class Program
    {
        [Verb("interactive", HelpText = "Interactive mode")]
        class InteractiveOptions { }

        [Verb("db2db", HelpText = "Database-to-database sync (n:n)")]
        class Db2dbOptions
        {
            [Option('i', "input", Required = true, HelpText = "Input database connection string")]
            public string InputConnectionString { get; set; }

            [Option('o', "output", Required = true, HelpText = "Output database connection string")]
            public string OutputConnectionString { get; set; }

            [Option('d', "databases", Required = true, HelpText = "Databases to sync (can be more than 1)", Separator = ',')]
            public string[] Databases { get; set; }

            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string WorkingDirectory { get; set; }

            [Option('u', "local-user", Required = false, HelpText = "Local user to give db_owner access after sync")]
            public string LocalUser { get; set; }
        }

        [Verb("db2f", HelpText = "Database-to-file sync (1:1)")]
        class Db2fOptions
        {
            [Option('i', "input", Required = true, HelpText = "Input database connection string")]
            public string InputConnectionString { get; set; }

            [Option('o', "output-file", Required = true, HelpText = "Output file (.bacpac format)")]
            public string OutputFile { get; set; }
            
            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string WorkingDirectory { get; set; }

            [Option('d', "database", Required = true, HelpText = "Database to sync")]
            public string Database { get; set; }
        }

        [Verb("f2db", HelpText = "File-to-database sync (1:1)")]
        class F2dbOptions
        {
            [Option('i', "input-file", Required = true, HelpText = "Input file (.bacpac format)")]
            public string InputFile { get; set; }

            [Option('o', "output", Required = true, HelpText = "Output database connection string")]
            public string OutputConnectionString { get; set; }
            
            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string WorkingDirectory { get; set; }

            [Option('d', "database", Required = true, HelpText = "Database to sync")]
            public string Database { get; set; }

            [Option('u', "local-user", Required = false, HelpText = "Local user to give db_owner access after sync")]
            public string LocalUser { get; set; }
        }

        static void Main(string[] args)
        {
            var parseResult = Parser.Default.ParseArguments<InteractiveOptions, Db2dbOptions, Db2fOptions, F2dbOptions>(args);

            parseResult.MapResult(
                (InteractiveOptions opts) => InteractiveSync(opts),
                (Db2dbOptions opts) => DatabaseToDatabaseSync(opts),
                (Db2fOptions opts) => DatabaseToFileSync(opts),
                (F2dbOptions opts) => FileToDatabaseSync(opts),
                errs => 1);
        }

        private static int InteractiveSync(InteractiveOptions opts)
        {
            // Interactive mode
            Console.WriteLine("--- WARNING ---");
            Console.WriteLine("Local databases for the selected profile will be overwritten! Ctrl+C out NOW if you'd like to keep them!");
            Console.WriteLine();

            Console.WriteLine("Select project profile to run:");
            var i = 1;

            var profiles = ProjectProfile.List().ToList();

            foreach (var p in profiles)
            {
                Console.WriteLine($"[{i++}] {p.Name}");
            }

            Console.WriteLine($"[{i}] Exit");

            var k = Console.ReadKey();

            if (!int.TryParse(k.KeyChar.ToString(), out var selectedIdx))
            {
                return 0;
            }

            if (profiles.Count < selectedIdx)
            {
                return 0;
            }

            var selectedProfile = profiles[selectedIdx - 1];

            DatabaseToDatabaseSync(new Db2dbOptions
            {
                InputConnectionString = selectedProfile.FromConnectionString,
                OutputConnectionString = selectedProfile.ToConnectionString,
                Databases = selectedProfile.DatabasesToSync,
                WorkingDirectory = selectedProfile.WorkingDirectory,
                LocalUser = selectedProfile.LocalDbUser
            });

            return 0;
        }

        private static int DatabaseToDatabaseSync(Db2dbOptions opts)
        {
            if (string.IsNullOrEmpty(opts.WorkingDirectory))
            {
                opts.WorkingDirectory = Environment.CurrentDirectory;
            }

            foreach (var db in opts.Databases)
            {
                var outputFile = Path.Combine(opts.WorkingDirectory, $"{db}.bacpac");

                DatabaseToFileSync(new Db2fOptions
                {
                    InputConnectionString = opts.InputConnectionString,
                    Database = db,
                    OutputFile = outputFile,
                    WorkingDirectory = opts.WorkingDirectory
                });

                FileToDatabaseSync(new F2dbOptions
                {
                    InputFile = outputFile,
                    OutputConnectionString = opts.OutputConnectionString,
                    Database = db,
                    LocalUser = opts.LocalUser,
                    WorkingDirectory = opts.WorkingDirectory
                });
            }

            return 0;
        }

        private static int DatabaseToFileSync(Db2fOptions opts)
        {
            if (string.IsNullOrEmpty(opts.WorkingDirectory))
            {
                opts.WorkingDirectory = Environment.CurrentDirectory;
            }

            var azureConnectionString = opts.InputConnectionString;
            var db = opts.Database;

            Console.WriteLine($"Fetching {db}...");
            Console.WriteLine();

            var dir = new FileInfo(opts.OutputFile).DirectoryName;

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dac = new DacServices(azureConnectionString);

            dac.ProgressChanged += (sender, eventArgs) => { Console.WriteLine($"[{db}] {eventArgs.Message}"); };

            try
            {
                dac.ExportBacpac(opts.OutputFile, db, DacSchemaModelStorageType.File);
            }
            catch (DacServicesException dex)
            {
                if (dex.InnerException == null)
                    throw;

                throw new DacServicesException(dex.InnerException.Message, dex);
            }

            Console.WriteLine($"[{db}] Export completed");

            return 0;
        }

        private static int FileToDatabaseSync(F2dbOptions opts)
        {
            if (string.IsNullOrEmpty(opts.WorkingDirectory))
            {
                opts.WorkingDirectory = Environment.CurrentDirectory;
            }

            var db = opts.Database;
            var pk = BacPackage.Load(opts.InputFile);
            var spec = new DacAzureDatabaseSpecification
            {
                Edition = DacAzureEdition.Basic
            };

            using (var sqlConn = new SqlConnection(opts.OutputConnectionString))
            using (var singleUserCmd = new SqlCommand($"IF db_id('{db}') is not null ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", sqlConn))
            using (var dropCmd = new SqlCommand($"IF db_id('{db}') is not null DROP DATABASE [{db}]", sqlConn))
            {
                sqlConn.Open();

                singleUserCmd.ExecuteNonQuery();
                dropCmd.ExecuteNonQuery();
            }

            var local = new DacServices(opts.OutputConnectionString);
            local.ProgressChanged += (sender, eventArgs) => { Console.WriteLine($"[{db}] {eventArgs.Message}"); };

            local.ImportBacpac(pk, db, spec);

            if (!string.IsNullOrEmpty(opts.LocalUser))
            {
                using (var sqlConn = new SqlConnection(opts.OutputConnectionString))
                using (var loginCmd = new SqlCommand($"USE [{db}]; CREATE USER [{opts.LocalUser}] FOR LOGIN [{opts.LocalUser}]; USE [{db}]; ALTER ROLE [db_owner] ADD MEMBER [{opts.LocalUser}];", sqlConn))
                {
                    sqlConn.Open();

                    try
                    {
                        loginCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: Couldn't add user {opts.LocalUser} because: {ex.Message}");
                    }
                }
            }

            Console.Write("done.");
            Console.WriteLine();

            return 0;
        }
    }
}

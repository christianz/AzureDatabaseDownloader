using System.Data;
using System.Globalization;
using CommandLine;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

namespace AzureDatabaseDownloader
{
    internal class Program
    {
        [Verb("interactive", HelpText = "Interactive mode")]
        class InteractiveOptions { }

        [Verb("db2db", HelpText = "Database-to-database sync (n:n)")]
        class Db2dbOptions
        {
            [Option('i', "input", Required = true, HelpText = "Input database connection string")]
            public string InputConnectionString { get; set; } = string.Empty;

            [Option('o', "output", Required = true, HelpText = "Output database connection string")]
            public string OutputConnectionString { get; set; } = string.Empty;

            [Option('d', "databases", Required = true, HelpText = "Databases to sync (can be more than 1)", Separator = ',')]
            public IEnumerable<string> Databases { get; set; } = [];

            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string? WorkingDirectory { get; set; }

            [Option('u', "local-user", Required = false, HelpText = "Local user to give db_owner access after sync")]
            public string? LocalUser { get; set; }

            [Option('e', "exclude-tables", Required = false, HelpText = "Tables to exclude from sync", Separator = ',')]
            public string[]? ExcludeTables { get; set; }
        }

        [Verb("db2f", HelpText = "Database-to-file sync (1:1)")]
        class Db2fOptions
        {
            [Option('i', "input", Required = true, HelpText = "Input database connection string")]
            public string InputConnectionString { get; set; } = string.Empty;

            [Option('o', "output-file", Required = true, HelpText = "Output file (.bacpac format)")]
            public string OutputFile { get; set; } = string.Empty;

            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string? WorkingDirectory { get; set; }

            [Option('d', "database", Required = true, HelpText = "Database to sync")]
            public string Database { get; set; } = string.Empty;

            [Option('e', "exclude-tables", Required = false, HelpText = "Tables to exclude from sync", Separator = ',')]
            public string[]? ExcludeTables { get; set; }
        }

        [Verb("f2db", HelpText = "File-to-database sync (1:1)")]
        class F2dbOptions
        {
            [Option('i', "input-file", Required = true, HelpText = "Input file (.bacpac format)")]
            public string InputFile { get; set; } = string.Empty;

            [Option('o', "output", Required = true, HelpText = "Output database connection string")]
            public string OutputConnectionString { get; set; } = string.Empty;

            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string? WorkingDirectory { get; set; }

            [Option('d', "database", Required = true, HelpText = "Database to sync")]
            public string Database { get; set; } = string.Empty;

            [Option('u', "local-user", Required = false, HelpText = "Local user to give db_owner access after sync")]
            public string? LocalUser { get; set; }
        }

        static int Main(string[] args)
        {
            // DacFx does not support supplemental Windows locales (culture 0x1000).
            // Force a well-known culture to prevent DacServicesException.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var parseResult = Parser.Default.ParseArguments<InteractiveOptions, Db2dbOptions, Db2fOptions, F2dbOptions>(args);

            return parseResult.MapResult(
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

            var profiles = ProjectProfile.List().ToList();

            for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                Console.WriteLine($"[{GetProfileSelectionKey(profileIndex)}] {profiles[profileIndex].Name}");
            }

            Console.WriteLine("[0] Exit");
            Console.Write("Selection: ");

            var selection = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(selection) || selection.Trim() == "0")
            {
                return 0;
            }

            var selectedIdx = ParseProfileSelection(selection, profiles.Count);

            if (selectedIdx == null)
            {
                Console.WriteLine("No profile selected.");
                return 1;
            }

            var selectedProfile = profiles[selectedIdx.Value];

            DatabaseToDatabaseSync(new Db2dbOptions
            {
                InputConnectionString = selectedProfile.FromConnectionString,
                OutputConnectionString = selectedProfile.ToConnectionString,
                Databases = selectedProfile.DatabasesToSync,
                WorkingDirectory = selectedProfile.WorkingDirectory,
                LocalUser = selectedProfile.LocalDbUser,
                ExcludeTables = selectedProfile.ExcludeTables,
            });

            return 0;
        }

        private static string GetProfileSelectionKey(int profileIndex)
        {
            return profileIndex < 9
                ? (profileIndex + 1).ToString()
                : ((char)('A' + profileIndex - 9)).ToString();
        }

        private static int? ParseProfileSelection(string? selection, int profileCount)
        {
            selection = selection?.Trim();

            if (string.IsNullOrEmpty(selection))
            {
                return null;
            }

            if (int.TryParse(selection, out var numericSelection)
                && numericSelection >= 1
                && numericSelection <= Math.Min(profileCount, 9))
            {
                return numericSelection - 1;
            }

            if (selection.Length == 1 && char.IsLetter(selection[0]))
            {
                var selectedIndex = char.ToUpperInvariant(selection[0]) - 'A' + 9;

                if (selectedIndex >= 0 && selectedIndex < profileCount)
                {
                    return selectedIndex;
                }
            }

            return null;
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
                    WorkingDirectory = opts.WorkingDirectory,
                    ExcludeTables = opts.ExcludeTables
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

            var dir = Path.GetDirectoryName(Path.GetFullPath(opts.OutputFile));

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dac = new DacServices(azureConnectionString);

            dac.ProgressChanged += (sender, eventArgs) => { Console.WriteLine($"[{db}] {eventArgs.Message}"); };

            try
            {
                List<Tuple<string, string>>? includeTables = null;

                if (opts.ExcludeTables != null)
                {
                    includeTables = GetTablesToInclude(opts.InputConnectionString, opts.ExcludeTables);
                }

                dac.ExportBacpac(opts.OutputFile, db, includeTables);
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

        private static List<Tuple<string, string>> GetTablesToInclude(string connectionString, string[] tablesToExclude)
        {
            using var connection = new SqlConnection(connectionString);

            connection.Open();

            using var command = new SqlCommand("SELECT * FROM INFORMATION_SCHEMA.TABLES", connection);
            using var reader = command.ExecuteReader();

            var includeTables = new List<Tuple<string, string>>();

            while (reader.Read())
            {
                var schemaName = Convert.ToString(reader["TABLE_SCHEMA"]) ?? string.Empty;
                var tableName = Convert.ToString(reader["TABLE_NAME"]) ?? string.Empty;
                var tableType = Convert.ToString(reader["TABLE_TYPE"]) ?? string.Empty;

                if (tableType != "BASE TABLE" || tablesToExclude.Contains($"{schemaName}.{tableName}"))
                {
                    continue;
                }

                includeTables.Add(new(schemaName, tableName));
            }

            return includeTables;
        }

        private static int FileToDatabaseSync(F2dbOptions opts)
        {
            if (string.IsNullOrEmpty(opts.WorkingDirectory))
            {
                opts.WorkingDirectory = Environment.CurrentDirectory;
            }

            var db = opts.Database;
            var pk = BacPackage.Load(opts.InputFile);

            var quotedDatabaseName = QuoteSqlIdentifier(db, nameof(opts.Database));

            using (var sqlConn = new SqlConnection(opts.OutputConnectionString))
            using (var singleUserCmd = new SqlCommand($"IF DB_ID(@DatabaseName) IS NOT NULL ALTER DATABASE {quotedDatabaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE", sqlConn))
            using (var dropCmd = new SqlCommand($"IF DB_ID(@DatabaseName) IS NOT NULL DROP DATABASE {quotedDatabaseName}", sqlConn))
            {
                singleUserCmd.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = db;
                dropCmd.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = db;

                sqlConn.Open();

                singleUserCmd.ExecuteNonQuery();
                dropCmd.ExecuteNonQuery();
            }

            var local = new DacServices(opts.OutputConnectionString);
            local.ProgressChanged += (sender, eventArgs) => { Console.WriteLine($"[{db}] {eventArgs.Message}"); };

            var spec = new DacAzureDatabaseSpecification
            {
                Edition = DacAzureEdition.Default,
                MaximumSize = 250,
                ServiceObjective = "S0"
            };

            local.ImportBacpac(pk, db, spec);

            if (!string.IsNullOrEmpty(opts.LocalUser))
            {
                var quotedLocalUser = QuoteSqlIdentifier(opts.LocalUser, nameof(opts.LocalUser));

                using var sqlConn = new SqlConnection(opts.OutputConnectionString);
                using var loginCmd = new SqlCommand($"USE {quotedDatabaseName}; CREATE USER {quotedLocalUser} FOR LOGIN {quotedLocalUser}; ALTER ROLE [db_owner] ADD MEMBER {quotedLocalUser};", sqlConn);

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

            Console.Write("done.");
            Console.WriteLine();

            return 0;
        }

        private static string QuoteSqlIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("SQL identifiers cannot be empty.", parameterName);
            }

            return $"[{value.Replace("]", "]]")}]";
        }
    }
}

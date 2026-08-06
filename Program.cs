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

            [Option('d', "databases", Required = true, HelpText = "Databases to sync (can be more than 1). Use \"source:destination\" to restore under a different name, e.g. \"MyDb:MyDb_Local\"", Separator = ',')]
            public IEnumerable<string> Databases { get; set; } = [];

            [Option('w', "working-dir", Required = false, HelpText = "Working directory (current directory is default)")]
            public string? WorkingDirectory { get; set; }

            [Option('u', "local-user", Required = false, HelpText = "Local user to give db_owner access after sync")]
            public string? LocalUser { get; set; }

            // Sequence options must be IEnumerable<T> rather than T[]: CommandLineParser 2.9.1
            // throws when it defaults an omitted sequence option whose property type is not generic.
            [Option('e', "exclude-tables", Required = false, HelpText = "Tables to exclude from sync (must include schema, e.g. \"dbo.Logs\")", Separator = ',')]
            public IEnumerable<string>? ExcludeTables { get; set; }

            [Option('m', "masking-script", Required = false, HelpText = "PII anonymization .sql run against the local target after import (applied to every synced database)")]
            public string? MaskingScript { get; set; }

            // Project-level stored procedures to EXEC after import, applied to every synced
            // database. Set from the CLI option or, in interactive mode, from the profile.
            [Option('p', "post-import-procedures", Required = false, HelpText = "Stored procedures to EXEC against the local target after import (applied to every synced database)", Separator = ';')]
            public IEnumerable<string>? PostImportProcedures { get; set; }

            // Per-database masking scripts (database name -> .sql path). Populated from a profile in interactive mode; not a CLI option.
            public Dictionary<string, string>? MaskingScripts { get; set; }
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

            // Must be IEnumerable<T> rather than T[]: CommandLineParser 2.9.1 throws when it
            // defaults an omitted sequence option whose property type is not generic.
            [Option('e', "exclude-tables", Required = false, HelpText = "Tables to exclude from sync (must include schema, e.g. \"dbo.Logs\")", Separator = ',')]
            public IEnumerable<string>? ExcludeTables { get; set; }
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

            [Option('d', "database", Required = true, HelpText = "Database to create on the output server (does not have to match the name the .bacpac was exported from)")]
            public string Database { get; set; } = string.Empty;

            [Option('u', "local-user", Required = false, HelpText = "Local user to give db_owner access after sync")]
            public string? LocalUser { get; set; }

            [Option('m', "masking-script", Required = false, HelpText = "PII anonymization .sql run against the local target after import")]
            public string? MaskingScript { get; set; }

            // IEnumerable<T> rather than T[]: see the note on Db2dbOptions.ExcludeTables.
            [Option('p', "post-import-procedures", Required = false, HelpText = "Stored procedures to EXEC against the local target after import", Separator = ';')]
            public IEnumerable<string>? PostImportProcedures { get; set; }
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

            Console.WriteLine();
            Console.WriteLine($"Syncing profile '{selectedProfile.Name}':");

            foreach (var db in selectedProfile.DatabasesToSync)
            {
                Console.WriteLine($"  {db}");
            }

            Console.WriteLine();

            DatabaseToDatabaseSync(new Db2dbOptions
            {
                InputConnectionString = selectedProfile.FromConnectionString,
                OutputConnectionString = selectedProfile.ToConnectionString,
                WorkingDirectory = selectedProfile.WorkingDirectory,
                LocalUser = selectedProfile.LocalDbUser,
                ExcludeTables = selectedProfile.ExcludeTables,
                MaskingScripts = selectedProfile.MaskingScripts,
                PostImportProcedures = selectedProfile.PostImportProcedures,
            }, selectedProfile.DatabasesToSync);

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
            List<DatabaseSyncItem> databases;

            try
            {
                databases = opts.Databases.Select(DatabaseSyncItem.Parse).ToList();
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                return 1;
            }

            return DatabaseToDatabaseSync(opts, databases);
        }

        private static int DatabaseToDatabaseSync(Db2dbOptions opts, IEnumerable<DatabaseSyncItem> databases)
        {
            if (string.IsNullOrEmpty(opts.WorkingDirectory))
            {
                opts.WorkingDirectory = Environment.CurrentDirectory;
            }

            foreach (var db in databases)
            {
                // The .bacpac is a copy of the source, so it keeps the source name even when the
                // destination database is named differently.
                var outputFile = Path.Combine(opts.WorkingDirectory, $"{db.Source}.bacpac");

                DatabaseToFileSync(new Db2fOptions
                {
                    InputConnectionString = opts.InputConnectionString,
                    Database = db.Source,
                    OutputFile = outputFile,
                    WorkingDirectory = opts.WorkingDirectory,
                    ExcludeTables = opts.ExcludeTables
                });

                // Per-database masking script (interactive/profile) takes precedence over the
                // single --masking-script applied to all databases.
                var maskingScript = ResolveMaskingScript(opts.MaskingScripts, db) ?? opts.MaskingScript;

                FileToDatabaseSync(new F2dbOptions
                {
                    InputFile = outputFile,
                    OutputConnectionString = opts.OutputConnectionString,
                    Database = db.Destination,
                    LocalUser = opts.LocalUser,
                    WorkingDirectory = opts.WorkingDirectory,
                    MaskingScript = maskingScript,
                    // Project-level procedures apply to every synced database.
                    PostImportProcedures = opts.PostImportProcedures
                });
            }

            return 0;
        }

        /// <summary>
        /// Finds the per-database masking script for a database being synced. Keyed on the source
        /// name first (the script is written against the source schema), falling back to the
        /// destination name so a renamed database still gets masked either way it was configured.
        /// </summary>
        private static string? ResolveMaskingScript(Dictionary<string, string>? maskingScripts, DatabaseSyncItem db)
        {
            if (maskingScripts == null)
            {
                return null;
            }

            if (maskingScripts.TryGetValue(db.Source, out var bySource))
            {
                return bySource;
            }

            return maskingScripts.TryGetValue(db.Destination, out var byDestination)
                ? byDestination
                : null;
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

                // CommandLineParser hands back an empty sequence (not null) when -e is omitted.
                if (opts.ExcludeTables?.Any() == true)
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

        private static List<Tuple<string, string>> GetTablesToInclude(string connectionString, IEnumerable<string> tablesToExclude)
        {
            var excluded = new HashSet<string>(tablesToExclude, StringComparer.Ordinal);

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

                if (tableType != "BASE TABLE" || excluded.Contains($"{schemaName}.{tableName}"))
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

            Console.WriteLine($"Restoring {Path.GetFileName(opts.InputFile)} into {db}...");
            Console.WriteLine();

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

            var masked = ApplyMaskingScript(opts.OutputConnectionString, db, quotedDatabaseName, opts.MaskingScript);
            var ranProcedures = ApplyPostImportProcedures(opts.OutputConnectionString, db, quotedDatabaseName, opts.PostImportProcedures);

            // If any sanitizing step ran, shred the raw export so unmasked/unpurged source
            // data does not linger on disk.
            if ((masked || ranProcedures) && !string.IsNullOrEmpty(opts.InputFile) && File.Exists(opts.InputFile))
            {
                File.Delete(opts.InputFile);
                Console.WriteLine($"[{db}] Deleted raw export {Path.GetFileName(opts.InputFile)}");
            }

            Console.Write("done.");
            Console.WriteLine();

            return 0;
        }

        /// <summary>
        /// Runs a PII anonymization script against the freshly imported LOCAL database.
        /// No-op (returns false) when no script is supplied. Never touches the source database.
        /// </summary>
        private static bool ApplyMaskingScript(string outputConnectionString, string db, string quotedDatabaseName, string? maskingScript)
        {
            if (string.IsNullOrWhiteSpace(maskingScript))
            {
                return false;
            }

            if (!File.Exists(maskingScript))
            {
                throw new FileNotFoundException($"Masking script not found: {maskingScript}");
            }

            Console.WriteLine($"[{db}] Applying masking script {Path.GetFileName(maskingScript)}...");

            var sql = File.ReadAllText(maskingScript);

            using (var conn = new SqlConnection(outputConnectionString))
            {
                conn.Open();

                foreach (var batch in SplitOnGo(sql))
                {
                    using var cmd = new SqlCommand($"USE {quotedDatabaseName};\n{batch}", conn)
                    {
                        CommandTimeout = 0 // masking can touch large tables; no timeout
                    };

                    cmd.ExecuteNonQuery();
                }
            }

            Console.WriteLine($"[{db}] Masking complete");
            return true;
        }

        /// <summary>
        /// EXECs the configured stored procedures against the freshly imported LOCAL database,
        /// after any masking. Each entry is run as "EXEC &lt;entry&gt;" so it may include arguments
        /// (e.g. "dbo.MyCleanup @Confirm = 1"). Returns false when none are supplied. Never
        /// touches the source database.
        /// </summary>
        private static bool ApplyPostImportProcedures(string outputConnectionString, string db, string quotedDatabaseName, IEnumerable<string>? procedures)
        {
            // CommandLineParser hands back an empty sequence (not null) when -p is omitted.
            if (procedures?.Any(p => !string.IsNullOrWhiteSpace(p)) != true)
            {
                return false;
            }

            using var conn = new SqlConnection(outputConnectionString);
            conn.Open();

            foreach (var proc in procedures)
            {
                if (string.IsNullOrWhiteSpace(proc))
                {
                    continue;
                }

                Console.WriteLine($"[{db}] Running post-import procedure: EXEC {proc}");

                using var cmd = new SqlCommand($"USE {quotedDatabaseName};\nEXEC {proc};", conn)
                {
                    CommandTimeout = 0 // a purge/cleanup proc can touch large tables; no timeout
                };

                cmd.ExecuteNonQuery();
            }

            Console.WriteLine($"[{db}] Post-import procedures complete");
            return true;
        }

        /// <summary>
        /// Splits a T-SQL script into batches on lines containing only "GO" (SSMS convention,
        /// not valid T-SQL). Scripts with no GO separators run as a single batch.
        /// </summary>
        private static IEnumerable<string> SplitOnGo(string sql)
        {
            var batches = System.Text.RegularExpressions.Regex.Split(
                sql,
                @"^\s*GO\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var batch in batches)
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    yield return batch;
                }
            }
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

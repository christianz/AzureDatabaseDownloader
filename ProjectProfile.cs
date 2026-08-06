using Newtonsoft.Json;

namespace AzureDatabaseDownloader
{
    internal class ProjectProfile
    {
        private const string ProfilePath = "profiles.json";

        public string Name { get; set; } = string.Empty;
        public string FromConnectionString { get; set; } = string.Empty;
        public string ToConnectionString { get; set; } = string.Empty;
        public DatabaseSyncItem[] DatabasesToSync { get; set; } = [];
        public string? LocalDbUser { get; set; }
        public string? WorkingDirectory { get; set; }
        public bool IsActive { get; set; }
        public string[]? ExcludeTables { get; set; }

        /// <summary>
        /// Optional per-database PII anonymization scripts (database name -> .sql path),
        /// run against the local target immediately after import. Prod is never touched.
        /// </summary>
        public Dictionary<string, string>? MaskingScripts { get; set; }

        /// <summary>
        /// Optional stored procedures to EXEC against the local target after import (and after
        /// any masking script), applied to every database this profile syncs. Each entry runs
        /// as EXEC &lt;entry&gt;, so it may include arguments, e.g. [ "dbo.MyCleanup @Confirm = 1" ].
        /// </summary>
        public string[]? PostImportProcedures { get; set; }

        public static IEnumerable<ProjectProfile> List()
        {
            if (!File.Exists(ProfilePath))
            {
                throw new FileNotFoundException("Couldn't find profiles.json. This file is required when running in interactive mode. Please copy profiles.sample.json to profiles.json and add your sync profiles there.");
            }

            var strProfiles = File.ReadAllText(ProfilePath);

            var profiles = (JsonConvert.DeserializeObject<List<ProjectProfile>>(strProfiles) ?? [])
                .Where(p => p.IsActive);

            return profiles;
        }
    }
}

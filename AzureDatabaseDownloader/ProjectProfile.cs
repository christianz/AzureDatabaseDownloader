using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AzureDatabaseDownloader
{
    public class ProjectProfile
    {
        private const string ProfilePath = "profiles.json";

        public string Name { get; set; }
        public string FromConnectionString { get; set; }
        public string ToConnectionString { get; set; }
        public string[] DatabasesToSync { get; set; }
        public string LocalDbUser { get; set; }
        public string WorkingDirectory { get; set; }
        public bool IsActive { get; set; }

        public static IEnumerable<ProjectProfile> List()
        {
            if (!File.Exists(ProfilePath))
            {
                throw new FileNotFoundException("Couldn't find profiles.json. This file is required when running in interactive mode. Please copy profiles.sample.json to profiles.json and add your sync profiles there.");
            }

            var strProfiles = File.ReadAllText(ProfilePath);

            var profiles = JsonConvert.DeserializeObject<List<ProjectProfile>>(strProfiles).Where(p => p.IsActive);

            return profiles;
        }
    }
}

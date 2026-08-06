using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AzureDatabaseDownloader
{
    /// <summary>
    /// A database to sync. The destination may be named differently from the source,
    /// e.g. KonstaliManagement (source) restored as KonstaliDevelopment_Today (destination).
    /// </summary>
    [JsonConverter(typeof(DatabaseSyncItemConverter))]
    internal sealed class DatabaseSyncItem
    {
        public const char NameSeparator = ':';

        public DatabaseSyncItem(string source, string? destination = null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Source database name cannot be empty.", nameof(source));
            }

            Source = source.Trim();
            Destination = string.IsNullOrWhiteSpace(destination) ? Source : destination.Trim();
        }

        public string Source { get; }
        public string Destination { get; }

        public bool IsRenamed => !string.Equals(Source, Destination, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parses "MyDatabase" (same name on both sides) or "MySourceDb:MyDestinationDb" (renamed).
        /// </summary>
        public static DatabaseSyncItem Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Database name cannot be empty.", nameof(value));
            }

            var separatorIndex = value.IndexOf(NameSeparator);

            if (separatorIndex < 0)
            {
                return new DatabaseSyncItem(value);
            }

            var source = value[..separatorIndex];
            var destination = value[(separatorIndex + 1)..];

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            {
                throw new ArgumentException($"'{value}' is not a valid database mapping. Expected \"source{NameSeparator}destination\".", nameof(value));
            }

            return new DatabaseSyncItem(source, destination);
        }

        public override string ToString() => IsRenamed ? $"{Source} -> {Destination}" : Source;
    }

    /// <summary>
    /// Reads a database to sync either as a plain string ("MyDb" or "MySourceDb:MyDestinationDb")
    /// or as an object ({ "source": "MySourceDb", "destination": "MyDestinationDb" }).
    /// </summary>
    internal sealed class DatabaseSyncItemConverter : JsonConverter<DatabaseSyncItem>
    {
        public override DatabaseSyncItem? ReadJson(JsonReader reader, Type objectType, DatabaseSyncItem? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            return token.Type switch
            {
                JTokenType.Null => null,
                JTokenType.String => DatabaseSyncItem.Parse(token.Value<string>()!),
                JTokenType.Object => ReadObject((JObject)token),
                _ => throw new JsonSerializationException($"Unexpected token '{token.Type}' while reading a database to sync. Expected a string or an object.")
            };
        }

        private static DatabaseSyncItem ReadObject(JObject item)
        {
            var source = GetProperty(item, "source", "from", "name");
            var destination = GetProperty(item, "destination", "to", "as");

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new JsonSerializationException("A database to sync must specify \"source\".");
            }

            return new DatabaseSyncItem(source, destination);
        }

        private static string? GetProperty(JObject item, params string[] names)
        {
            return item.Properties()
                .FirstOrDefault(p => names.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                ?.Value.Value<string>();
        }

        public override void WriteJson(JsonWriter writer, DatabaseSyncItem? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (!value.IsRenamed)
            {
                writer.WriteValue(value.Source);
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("source");
            writer.WriteValue(value.Source);
            writer.WritePropertyName("destination");
            writer.WriteValue(value.Destination);
            writer.WriteEndObject();
        }
    }
}

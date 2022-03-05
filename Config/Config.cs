using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common
{
    using SeedGroups = Dictionary<string, Dictionary<uint, string>>;
    using SeedGroups2 = Dictionary<string, Dictionary<string, string>>;

    public class Config
    {
        public class App
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public string DictPath { get; set; }
            public class Dict
            {
                public string Name { get; set; }
                public string Path { get; set; }
                public string Sha256 { get; set; }
                public DbFileType? Type { get; set; }
                public string KeywordEncoding { get; set; }
                public class _Patches
                {
                    public Dictionary<string, uint> CorruptedWords { get; set; }
                    public string[] OrphanedWords { get; set; }
                    public Dictionary<string, object> CorruptedEntries { get; set; }
                    public class Substitution
                    {
                        public string[] Words { get; set; }
                        public string[] Targets { get; set; }
                        public string[] Replacements { get; set; }
                    }
                    public Substitution[] Substitutions { get; set; }
                }
                public _Patches Patches { get; set; }
            }
            public Dict[] Dicts { get; set; }
        }
        public App[] Apps { get; set; }
        public SeedGroups SeedsByName { get; set; }

        private Config() { }

        public static Config Get()
        {
            var jsonOptions = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());

            var apps = JsonSerializer.Deserialize<App[]>(File.ReadAllText("config.json"), jsonOptions);

            foreach (var crpEntries in apps.SelectMany(
                app => app.Dicts.Select(dict => dict.Patches?.CorruptedEntries ?? new Dictionary<string, object>())))
            {
                foreach (var (key, value) in crpEntries.ToArray())
                {
                    if (value is not JsonElement entry)
                        break;
                    else if (entry.ValueKind == JsonValueKind.String)
                        crpEntries[key] = entry.GetString();
                    else if (entry.ValueKind == JsonValueKind.Number)
                        crpEntries[key] = entry.GetInt32();
                }
            }

            var seedsByName2 = JsonSerializer.Deserialize<SeedGroups2>(File.ReadAllText("config-seeds.json"), jsonOptions);
            var seedsByName = seedsByName2.ToDictionary(
                seedGroup => seedGroup.Key, seedGroup => seedGroup.Value.ToDictionary(
                    seed => ParseNumber(seed.Key), seed => seed.Value
                )
            );

            return new Config {
                Apps = apps,
                SeedsByName = seedsByName,
            };
        }

        static uint ParseNumber(string str)
        {
            str = str.Trim();
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(str, 16);
            return uint.Parse(str);
        }
    }
}
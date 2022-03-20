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
                public string ShortName { get; set; }
                public string Path { get; set; }
                public string Sha256 { get; set; }
                public DbFileType? Type { get; set; }
                public string KeywordEncoding { get; set; }
                public string StyleSheet { get; set; }
                public bool UseEastAsianFont { get; set; }
                public bool UseMetaTitle { get; set; }
                public bool FixBulletPoint { get; set; }
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

        public class MarkupConfig
        {
            public string Meta { get; set; }
            public string Meta2 { get; set; }
            public string Definition { get; set; }
            public string Example { get; set; }
            public string ExampleTranslation { get; set; }
            public string PhoneticNotation { get; set; }
            public string Idiom { get; set; }
            public string IdiomTranslation { get; set; }
            public string IdiomExample { get; set; }
            public string IdiomExampleTranslation { get; set; }
            public string Alternative { get; set; }
            public string Tab { get; set; }
            public string Media { get; set; }
            public string MetaTitle { get; set; }
            public string NoBulletClass { get; set; }
            public string EastAsianTextClass { get; set; }
        }
        public MarkupConfig ConfigMarkup { get; set; }

        Config() { }

        static readonly Config config;
        static Config() => config = Open();
        public static Config Get() => config;
        static Config Open()
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

            var configMarkup = JsonSerializer.Deserialize<MarkupConfig>(File.ReadAllText("config-markup.json"), jsonOptions);

            return new Config {
                Apps = apps,
                SeedsByName = seedsByName,
                ConfigMarkup = configMarkup,
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
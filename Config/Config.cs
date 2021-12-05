using Common;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using SeedGroups = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<uint, string>>;
using SeedGroups2 = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>;

#pragma warning disable CA1050 // Declare types in namespaces
public class Config
#pragma warning restore CA1050 // Declare types in namespaces
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
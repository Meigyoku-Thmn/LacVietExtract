using Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ConvViVi
{
    class ViViProgram
    {
        class Entry
        {
            public uint Hash;
            public string Word;
            public string Content;
        }

        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.Unicode;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var config = Config.Get();
            var app = config.Apps.First(e => e.Name == "Lạc Việt mtd CVH");
            var dict = app.Dicts.First(e => e.Name == "Từ điển tiếng Việt");
            var seeds = config.SeedsByName[app.Name];

            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);
            var cloneDbPath = Path.Combine(app.Name, Path.GetFileName(dict.Path));
            Tools.PrepareDatabase(dbPath, cloneDbPath, dict.Type);

            var encoding = Encoding.GetEncoding(dict.KeywordEncoding);

            using var conWrapper = new FileConnection(cloneDbPath, dict.Type.Value);
            var con = conWrapper.conn as SQLiteConnection;

            var words = new HashSet<string>();
            var query = $@"
                WITH Hash(Id, Ord) AS (
                  {"VALUES" + string.Join(",", Enumerable.Range(0, 100)
                    .Select(ord => $"({Tools.HashKeyword(encoding.GetBytes($"m_block_{ord}"))},{ord})"))}
                )
                SELECT
                    mlob.ab     AS ab,
                    mlob.cd     AS cd, 
                    mblklen.cd  AS len
                FROM mlob
                JOIN mblklen ON mlob.ab = mblklen.ab
                JOIN Hash ON mlob.ab = Hash.id
                ORDER BY Hash.Ord
            ";
            using (var cmd = new SQLiteCommand(query, con))
            using (var reader = cmd.ExecuteReader())
            {
                var loopCount = 0;
                while (reader.Read())
                {
                    var hash = (uint)reader["ab"];
                    var data = reader["cd"] as byte[];
                    var encryptedSize = (int)(uint)reader["len"];
                    Tools.DecodeBinaryInPlace(data);
                    try
                    {
                        Tools.DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), seeds, allowSkip: true);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"{e.Message} Key = {hash}, Size = {data.Length}, Ordinal = {loopCount}");
                        continue;
                    }
                    var _words = encoding
                        .GetString(data.AsSpan(4, encryptedSize - 4))
                        .Split('\0', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var word in _words)
                        words.Add(word);

                    loopCount++;
                }
                Console.WriteLine($"Found\t{words.Count} indexes.");
            }

            var _entries = new Dictionary<uint, Entry>();
            var query2 = $@"
                SELECT
                    mabcdef.ab      AS ab,
                    mabcdef.cd      AS cd,
                    mabcdeflen.cd   AS len
                FROM mabcdef
                JOIN mabcdeflen ON mabcdef.ab = mabcdeflen.ab
                ORDER BY ab
            ";
            using (var cmd = new SQLiteCommand(query2, con))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var hash = (uint)reader["ab"];
                    var data = reader["cd"] as byte[];
                    var encryptedSize = (int)(uint)reader["len"];
                    Tools.DecodeBinaryInPlace(data);
                    Tools.DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), seeds);
                    var content = Encoding.Latin1.GetString(data.AsSpan(4, encryptedSize - 4));
                    content = Tools.ResolveLacVietMarkups(content);
                    _entries.Add(hash, new Entry {
                        Hash = hash,
                        Content = content,
                    });
                }
                Console.WriteLine($"Found\t{_entries.Count} entries.");
            }

            var orphanedWords = new List<string>();
            var wordPool = new Dictionary<uint, List<string>>(words.Count);
            foreach (var word in words)
            {
                var hash = Tools.HashKeyword(encoding.GetBytes(word));
                wordPool.TryGetValue(hash, out var list);
                if (list == null)
                    list = wordPool[hash] = new List<string>();
                list.Add(word);
                if (list.Count > 1)
                {
                    Console.WriteLine("Hash-duplicated words:");
                    list.ForEach(e => Console.WriteLine("\t" + e));
                }
                _entries.TryGetValue(hash, out var entry);
                if (entry == null)
                {
                    orphanedWords.Add(word);
                    continue;
                }
                if (entry.Word == null)
                    entry.Word = word;
            }
            if (orphanedWords.Count > 0)
            {
                Console.WriteLine("Orphaned words:");
                orphanedWords.ForEach(e => Console.WriteLine("\t" + e));
            }
            var entries = new Dictionary<string, Entry>(_entries.Count);
            foreach (var entry in _entries.Values)
            {
                if (entry.Word == null)
                {
                    Console.WriteLine($"({entry.Hash}) {entry.Content}");
                    entries.Add(entry.Hash.ToString(), entry);
                }
                else
                    entries.Add(entry.Word, entry);
            }

            Console.WriteLine("Guess Zn for 2877811582");
            entries["2877811582"].Word = "Zn";

            Console.WriteLine("Guess Hz for 3383316432");
            entries["3383316432"].Word = "Hz";

            Console.WriteLine("Guess Al for 3971957192");
            entries["3971957192"].Word = "Al";
        }
    }
}

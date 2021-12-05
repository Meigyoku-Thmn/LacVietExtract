using Common;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;

namespace ConvCnVi
{
    class Entry
    {
        public uint Hash;
        public string Word;
        public string Content;
        public bool Corrupted;
    }

    class CnViProgram
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.Unicode;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var config = Config.Get();
            var app = config.Apps.First(e => e.Name == "Lạc Việt mtd CVH");
            var dict = app.Dicts.First(e => e.Name == "Từ điển Trung-Việt");
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
                while (reader.Read())
                {
                    var hash = (uint)reader["ab"];
                    var data = reader["cd"] as byte[];
                    var encryptedSize = (int)(uint)reader["len"];
                    Tools.DecodeBinaryInPlace(data);
                    Tools.DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), seeds);

                    var _words = encoding
                        .GetString(data.AsSpan(4, encryptedSize - 4))
                        .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var word in _words)
                    {
                        if (word.StartsWith("自相") && word.EndsWith("残害"))
                        {
                            Console.WriteLine("Resolve a corrupted word: " + word);
                            foreach (var w in word.Split('?'))
                                words.Add(w);
                        }
                        else
                            words.Add(word);
                    }
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
                    var decrypted = Tools.DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), seeds, allowSkip: true);
                    var content = decrypted
                        ? Encoding.Latin1.GetString(data.AsSpan(4, encryptedSize - 4))
                        : Encoding.Latin1.GetString(data.AsSpan(0, encryptedSize));
                    try
                    {
                        content = Tools.ReduceGarbage(content);
                        content = Tools.ResolveLacVietMarkups(content);
                        _entries.Add(hash, new Entry {
                            Hash = hash,
                            Content = content,
                        });
                    }
                    catch
                    {
                        _entries.Add(hash, new Entry {
                            Hash = hash,
                            Content = content,
                            Corrupted = true,
                        });
                    }
                }
                Console.WriteLine($"Found\t{_entries.Count} entries.");

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
                    entry.Word = word;
                }

                if (orphanedWords.Count > 0)
                {
                    Console.WriteLine("Orphaned words:");
                    orphanedWords.ForEach(e => Console.WriteLine("\t" + e));
                }
                var corruptedEntryCount = _entries.Values.Count(e => e.Corrupted);
                if (corruptedEntryCount > 0)
                    Console.WriteLine($"Corrupted Entry Count: {corruptedEntryCount}");
                var entries = new Dictionary<string, Entry>(_entries.Count);
                var orphanedCount = 0;
                foreach (var entry in _entries.Values)
                {
                    if (entry.Word == null)
                    {
                        orphanedCount++;
                        entries.Add(entry.Hash.ToString(), entry);
                    }
                    else
                        entries.Add(entry.Word, entry);
                }

                if (orphanedCount > 0)
                    Console.WriteLine($"Orphaned Entry Count: {orphanedCount}");

                Console.WriteLine("Guess 自相 for 228470692");
                entries["228470692"].Word = "自相";
            }
        }
    }
}

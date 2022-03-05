using Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Extractor
{
    using Seeds = Dictionary<uint, string>;
    using WordPool = Dictionary<uint, string>;
    using Words = Dictionary<string, uint>;
    using Entries = Dictionary<uint, Entry>;

    class Entry
    {
        public uint Hash;
        public string Word;
        public string Content;
        public string RawContent;
        public string[] ErrorMessages;
    }

    class ExtractorProgram
    {
        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var config = Config.Get();
#if DEBUG
            ModifyConfigForDebugging(config);
#endif

            try { Directory.Delete("Corrupted Entries", true); }
            catch (DirectoryNotFoundException) { }
            foreach (var app in config.Apps)
            {
                Log.Write($"Extracting '{app.Name}':");
                Log.IndentLevel++;
                foreach (var dict in app.Dicts)
                {
                    Log.Write($"{dict.Name}:");
                    Log.IndentLevel++;
                    ProcessDict(app, dict, config.SeedsByName[app.Name]);
                    Log.IndentLevel--;
                }
                Log.IndentLevel--;
            }
        }

#if DEBUG
        static void ModifyConfigForDebugging(Config config)
        {
            return;
        }
#endif

        static void ProcessDict(Config.App app, Config.App.Dict dict, Seeds seeds)
        {
            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);
            var encoding = Encoding.GetEncoding(dict.KeywordEncoding);
            encoding = Encoding.GetEncoding(dict.KeywordEncoding,
                encoding.EncoderFallback, new NullPreservingDecoderFallback());

            using var con = DictConnection.Create(dbPath, dict.Type.Value, encoding, app.Name, seeds);

            var words = new Words();
            foreach (var (hash, word) in con.ReadWords())
                words.TryAdd(word, hash);
            Log.Write($"Found {words.Count} words.");

            var entries = new Entries(words.Count);
            foreach (var (hash, _content) in con.ReadEntries())
            {
                var (content, errorMessages) = Helper.ResolveLacVietMarkups(_content);
                if (entries.TryAdd(hash, new Entry {
                    Hash = hash,
                    Content = content,
                    ErrorMessages = errorMessages,
                    RawContent = _content,
                })) continue;
                if (entries[hash].Content != content)
                    throw new Exception($"Detected entries that have same hash ({hash}) but different contents!");
            }
            Log.Write($"Found {entries.Count} entries.");

            Log.Write("Apply patches:");
            Log.IndentLevel++;
            Patching.Apply(dict, words, entries);
            Log.IndentLevel--;

            // detect hash-duplicated words
            // detect orphaned words
            // assign words to corresponding entries
            var wordPool = new WordPool(words.Count);
            var duplicatedWordGroupByHash = new Dictionary<uint, List<string>>();
            var orphanedWords = new List<string>();
            foreach (var (word, hash) in words)
            {
                if (!wordPool.TryAdd(hash, word))
                {
                    duplicatedWordGroupByHash.TryGetValue(hash, out var duplicatedWords);
                    if (duplicatedWords == null)
                        duplicatedWords = duplicatedWordGroupByHash[hash] = new List<string>();
                    var firstWord = wordPool[hash];
                    if (firstWord != "")
                    {
                        duplicatedWords.Add(firstWord);
                        wordPool[hash] = "";
                    }
                    duplicatedWords.Add(word);
                }

                entries.TryGetValue(hash, out var entry);
                if (entry == null)
                    orphanedWords.Add(word);
                else
                    entry.Word = word;
            }

            if (duplicatedWordGroupByHash.Count > 0)
            {
                Log.Write($"There are {duplicatedWordGroupByHash.Count} duplicated word groups:");
                Log.IndentLevel++;
                foreach (var (hash, duplicatedWords) in duplicatedWordGroupByHash)
                {
                    Log.Write($"Hash {hash}:");
                    Log.IndentLevel++;
                    duplicatedWords.ForEach(e => Log.Write("- " + e));
                    Log.IndentLevel--;
                }
                Log.IndentLevel--;
            }

            if (orphanedWords.Count > 0)
            {
                Log.Write($"There are {orphanedWords.Count} orphaned words:");
                Log.IndentLevel++;
                orphanedWords.ForEach(e => Log.Write($"- {e} ({words[e]})"));
                Log.IndentLevel--;
            }

            var corruptedEntries = new List<Entry>();
            var orphanedEntries = new List<Entry>();
            foreach (var entry in entries.Values)
            {
                if (entry.Word == null)
                    orphanedEntries.Add(entry);

                if (entry.ErrorMessages?.Length > 0)
                    corruptedEntries.Add(entry);
            }

            if (orphanedEntries.Count > 0)
            {
                Log.Write($"There are {orphanedEntries.Count} orphaned entries:");
                Log.IndentLevel++;
                orphanedEntries.ForEach(e => {
                    Log.Write(e.Hash);
                    Log.Write(e.Content);
                    Console.WriteLine();
                });
                Log.IndentLevel--;
            }

            if (corruptedEntries.Count > 0)
            {
                Log.Write($"There are {corruptedEntries.Count} corrupted entries.");
                var errorDirPath = Path.Combine("Corrupted Entries", app.Name, dict.Name);
                Directory.CreateDirectory(errorDirPath);
                foreach (var corruptedEntry in corruptedEntries)
                {
                    var normalizedWord = "e_" + corruptedEntry.Word.ReplaceInvalidChars();
                    var outputPath = Path.Combine(errorDirPath, normalizedWord + "_cor.txt");
                    File.WriteAllLines(
                        Path.Combine(errorDirPath, normalizedWord + ".txt"),
                        new[] {
                            $"Hash: {corruptedEntry.Hash}",
                            $"Word: {corruptedEntry.Word}",
                            "Errors:",
                            string.Join(Environment.NewLine, corruptedEntry.ErrorMessages),
                            "",
                            "================Raw-Content-Begin================",
                            corruptedEntry.RawContent,
                            "================Raw-Content-End==================",
                            "=============Resolved-Content-Begin==============",
                            corruptedEntry.Content,
                            "=============Resolved-Content-End================",
                        }
                    );
                }
            }

            var outputDirPath = Path.Combine("All Entries", app.Name, dict.Name);
            Directory.CreateDirectory(outputDirPath);
            var outputFilePath = Path.Combine(outputDirPath, dict.Name + "_out.txt");
            var outputRawFilePath = Path.Combine(outputDirPath, dict.Name + "_raw.txt");
            using var writer = new StreamWriter(outputFilePath);
            using var rawWriter = new StreamWriter(outputRawFilePath);
            foreach (var entry in entries.Values)
            {
                writer.WriteLine($"Hash: {entry.Hash}");
                writer.WriteLine($"Word: {entry.Word}");
                writer.WriteLine(entry.Content);
                writer.WriteLine();
                rawWriter.WriteLine($"Hash: {entry.Hash}");
                rawWriter.WriteLine($"Word: {entry.Word}");
                rawWriter.WriteLine(entry.RawContent);
                rawWriter.WriteLine();
            }
        }
    }
}

using Common;
using Ionic.Zlib;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Extractor
{
    using Seeds = Dictionary<uint, string>;
    using WordPool = Dictionary<uint, string>;
    using Words = Dictionary<string, uint>;
    using Entries = Dictionary<uint, Entry>;

    class ExtractorProgram
    {
        static readonly Config config = Config.Get();
        static readonly PropertyInfo[] MarkupKeywords = config.ConfigMarkup.GetType().GetProperties().ToArray();
        static readonly Regex MarkupRegex = new(
            string.Join('|', MarkupKeywords.Select(p => p.Name).OrderByDescending(e => e.Length)),
            RegexOptions.Compiled);
        static readonly Dictionary<string, object> MarkupMap =
            new(MarkupKeywords.Select(p => KeyValuePair.Create(p.Name, p.GetValue(config.ConfigMarkup))));
        static void Main(string[] args)
        {
            var watch = new Stopwatch();
            watch.Start();
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#if DEBUG
            ModifyConfigForDebugging(config);
#endif

            try { Directory.Delete("Corrupted Entries", true); }
            catch (DirectoryNotFoundException) { }
            var hashPool = new HashSet<string>();
            foreach (var app in config.Apps)
            {
                Log.Write($"Extracting '{app.Name}':");
                Log.IndentLevel++;
                foreach (var dict in app.Dicts)
                {
                    if (hashPool.Contains(dict.Sha256))
                        continue;
                    hashPool.Add(dict.Sha256);
                    Log.Write($"{dict.Name}:");
                    try
                    {
                        Log.IndentLevel++;
                        ProcessDict(
                            Path.Combine(args.FirstOrDefault() ?? "output", "Lạc Việt - " + dict.Name),
                            app, dict, config.SeedsByName[app.Name]);
                        Log.IndentLevel--;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                Log.IndentLevel--;
            }
            watch.Stop();
            Console.WriteLine($"Time taken: {watch.Elapsed}");
        }

#if DEBUG
        static void ModifyConfigForDebugging(Config config)
        {
            config.Apps = config.Apps.Where(e => e.Name == "Lạc Việt mtd CVH").ToArray();
            config.Apps[0].Dicts = config.Apps[0].Dicts.Where(e => e.Name == "Từ điển Việt-Trung (Giản thể)").ToArray();
            return;
        }
#endif
        const string OutputCssFileName = "style.css";
        static readonly byte[] StyleRefBin = Encoding.UTF8.GetBytes(
            $"<link rel=\"stylesheet\" href=\"{OutputCssFileName}\"/>"
        );
        static readonly SHA256 sha256Hasher = SHA256.Create();
        static readonly Regex ControlCharRegex = new(@"[\x00-\x1F]", RegexOptions.Compiled);
        static readonly Dictionary<string, string> CssPool = new();
        static void ProcessDict(string outputDirPath, Config.App app, Config.App.Dict dict, Seeds seeds)
        {
            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);
            var encoding = Encoding.GetEncoding(dict.KeywordEncoding);
            encoding = Encoding.GetEncoding(dict.KeywordEncoding,
                encoding.EncoderFallback, new NullPreservingDecoderFallback());

            var fileHash = Convert.ToHexString(
                sha256Hasher.ComputeHash(new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)));
            if (!fileHash.Equals(dict.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"The database '{dict.Name}' ({dict.Path}) doesn't match the hash in configuration file.");

            var con = DictConnection.Create(dbPath, dict.Type.Value, encoding, app.Name, seeds);

            var words = new Words();
            foreach (var (hash, word) in con.ReadWords())
                words.TryAdd(word, hash);
            Log.Write($"Found {words.Count} words.");

            var nEntries = con.CountEntries();
            Console.Title = $"Queried 0 of {nEntries} entries";
            var entries = new Entries(nEntries);
            using (var prBar = new VtProgressBar() { Total = nEntries, Title = "Queried" })
            {
                prBar.Initialize();
                prBar.Tick(0);
                foreach (var (hash, content, idx) in con.ReadEntries().Select((e, i) => (e.hash, e.content, i)))
                {
                    if (entries.TryAdd(hash, new Entry {
                        Hash = hash,
                        Content = content,
                    }))
                    {
                        Console.Title = $"Queried {idx + 1} of {nEntries} entries";
                        prBar.Tick();
                        continue;
                    }
                    if (entries[hash].Content != content)
                        throw new Exception($"Detected entries that have same hash ({hash}) but different contents!");
                }
            }

            con.Dispose();

            Log.Write("Apply pre-patches:");
            Log.IndentLevel++;
            Patching.PreApply(dict, words, entries);
            Log.IndentLevel--;

            Console.Title = $"Processed 0 of {nEntries} entries";
            using (var prBar = new VtProgressBar() { Total = nEntries, Title = "Processed" })
            {
                prBar.Initialize();
                prBar.Tick(0);
                foreach (var (entry, idx) in entries.Values.Select((e, i) => (e, i)))
                {
                    var (content, errorMessages) = Markup.Resolve(entry.Content,
                        useMetaTitle: dict.UseMetaTitle,
                        fixBulletPoint: dict.FixBulletPoint,
                        useEastAsianFont: dict.UseEastAsianFont
                    );
                    entry.RawContent = entry.Content;
                    entry.Content = content;
                    entry.ErrorMessages = errorMessages;
                    Console.Title = $"Processed {idx + 1} of {nEntries} entries";
                    prBar.Tick();
                }
            }
            Log.Write($"Found {entries.Count} entries.");

            Log.Write("Apply post-patches:");
            Log.IndentLevel++;
            Patching.PostApply(dict, words, entries);
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

            var dictFileName = dict.ShortName + ".dict";
            var outputDictPath = Path.Combine(outputDirPath, dictFileName);
            var outputIdxPath = Path.Combine(outputDirPath, dict.ShortName + ".idx.gz");
            Directory.CreateDirectory(outputDirPath);

            long idxFileSize = 0;
            Console.Title = $"Wrote 0 of {entries.Count} entries";
            using (var prBar = new VtProgressBar() { Total = entries.Count, Title = "Wrote" })
            using (var dictStream = new FileStream(outputDictPath, FileMode.Create, FileAccess.Write))
            using (var indexStream = new GZipStream(new FileStream(outputIdxPath, FileMode.Create, FileAccess.Write),
                CompressionMode.Compress, CompressionLevel.BestCompression))
            {
                var buffer = new byte[sizeof(int)];
                var mappedSortedEntries = entries.Values.Select((e, i) => {
                    var word = ControlCharRegex.Replace(e.Word, "").Trim().Normalize(NormalizationForm.FormC);
                    var content = e.Content.Trim().Normalize(NormalizationForm.FormC);
                    return new {
                        idx = i,
                        e.Hash,
                        WordBin = Encoding.UTF8.GetBytes(word),
                        ContentBin = Encoding.UTF8.GetBytes(content),
                    };
                }).OrderBy(e => e.WordBin, BinArrayComparer.StarDict);

                prBar.Initialize();
                prBar.Tick(0);

                foreach (var entry in mappedSortedEntries)
                {
                    indexStream.Write(entry.WordBin);
                    indexStream.Write(new byte[] { 0 });

                    BinaryPrimitives.WriteInt32BigEndian(buffer, (int)dictStream.Position);
                    indexStream.Write(buffer);
                    BinaryPrimitives.WriteInt32BigEndian(buffer, StyleRefBin.Length + entry.ContentBin.Length);
                    indexStream.Write(buffer);

                    idxFileSize += entry.WordBin.Length + 9;

                    dictStream.Write(StyleRefBin);
                    dictStream.Write(entry.ContentBin);
                    Console.Title = $"Wrote {entry.idx + 1} of {entries.Count} entries";
                    prBar.Tick();
                }
            }

            var dictZipProc = Process.Start(new ProcessStartInfo("dictzip", $"\"{dictFileName}\"") {
                UseShellExecute = false,
                WorkingDirectory = outputDirPath,
            });
            dictZipProc.WaitForExit();
            if (dictZipProc.ExitCode != 0)
                throw new Exception("dictzip process has closed with the exit code " + dictZipProc.ExitCode);

            File.WriteAllLines(Path.Combine(outputDirPath, dict.ShortName + ".ifo"), new[] {
                "StarDict's dict ifo file",
                "version=2.4.2",
                $"wordcount={entries.Count}",
                $"idxfilesize={idxFileSize}",
                $"bookname=Lạc Việt - {dict.Name}",
                "author=Meigyoku Thmn",
                "email=tranhuynhminhngoc1994@gmail.com",
                $"description={dict.Name} được trích xuất từ bộ từ điển Lạc Việt đến hết ngày 26/11/2021",
                "sametypesequence=h",
            });

            Directory.CreateDirectory(Path.Combine(outputDirPath, "res"));

            CssPool.TryGetValue(dict.StyleSheet, out var cssContent);
            if (cssContent == null)
            {
                cssContent = File.ReadAllText(Path.Combine("Styles", dict.StyleSheet));
                cssContent = MarkupRegex.Replace(cssContent, m => MarkupMap[m.Value].ToString());
                CssPool[dict.StyleSheet] = cssContent;
            }
            File.WriteAllText(Path.Combine(outputDirPath, "res", OutputCssFileName), cssContent);
        }
    }
}

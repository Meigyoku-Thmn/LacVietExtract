using Common;
using HtmlAgilityPack;
using SharpScss;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace Extractor
{
    using static CompressionLevel;
    using Seeds = Dictionary<uint, string>;
    using Entries = Dictionary<uint, DiEntry>;
    using App = Config.App;
    using Dict = Config.App.Dict;
    using Extra = Config.App.Dict._Extra;

    public static class ExtraExtractor
    {
        static readonly Config config = Config.Get();
        static readonly HtmlDocument doc = new();
        static readonly Config.MarkupConfig Markups = config.ConfigMarkup;

        class Result
        {
            public string Word;
            public List<string> Contents = new();
        }
        public static void Process(App app, Dict dict, Seeds seeds, Entries entries)
        {
            var extraContents = new Dictionary<uint, Result>();
            void AddNewContents(IEnumerable<(uint hash, string word, string content)> items)
            {
                foreach (var (hash, word, content) in items)
                {
                    if (word == null)
                        continue;
                    if (!extraContents.TryGetValue(hash, out var rs))
                        rs = extraContents[hash] = new Result();
                    if (rs.Word == null)
                        rs.Word = word;
                    rs.Contents.Add(content);
                }
            }

            foreach (var (cat, extra) in dict.Extra?.AsEnumerable())
            {
                switch (cat)
                {
                    //case "thesaurus":
                    //    AddNewContents(ProcessThesaurus(app, dict, seeds, extra, entries));
                    //    break;
                    //case "relatedWords":
                    //    AddNewContents(ProcessRelatedWords(app, dict, seeds, extra, entries));
                    //    break;
                    //case "occurences":
                    //    AddNewContents(ProcessOccurences(app, dict, seeds, extra, entries));
                    //    break;
                    //case "images":
                    //    ProcessImageDict(app, dict, seeds, extra, entries);
                    //    break;
                    case "grammar":
                        ProcessGrammarDict(app, dict, seeds, extra, entries);
                        break;
                }
            }

            var nEntries = extraContents.Count;
            Console.Title = $"Processed 0 of {nEntries} extra entries";

            using var prBar = VtProgressBar.Create(nEntries, "Processed");
            var count = 1;
            foreach (var (hash, rs) in extraContents)
            {
                entries.TryGetValue(hash, out var entry);
                if (entry == null)
                    entries.Add(hash, new DiEntry {
                        Content = string.Concat(rs.Contents),
                        Hash = hash,
                        Word = rs.Word,
                    });
                else
                    entry.Content = string.Concat(rs.Contents.Prepend(entry.Content));
                Console.Title = $"Processed {count} of {nEntries} extra entries";
                prBar.Tick();
                count++;
            }
        }

        static readonly SHA256 sha256Hasher = SHA256.Create();
        static void ValidateFileHashes(string[] filePaths, string[] hashes)
        {
            for (var i = 0; i < filePaths.Length; i++)
            {
                var filePath = filePaths[i];
                var hash = hashes[i];
                var fileHash = Convert.ToHexString(sha256Hasher.ComputeHash(
                    new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)));
                if (!fileHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"The database {Path.GetFileName(filePath)} doesn't match the hash in configuration file.");
            }
        }

        const string AntonymPrefix = "antonym (phản nghĩa):";
        static readonly Regex PunctationRegex = new(@"  | ,", RegexOptions.Compiled);
        static readonly Regex HeadlineRegex = new(@"(.+?) \(.+? - .+?\)", RegexOptions.Compiled);
        class ThesaurusItem
        {
            public string HeadLine;
            public List<string> Synonyms = new();
            public List<string> Antonyms = new();
        }
        static IEnumerable<(uint hash, string word, string content)>
            ProcessThesaurus(App app, Dict dict, Seeds seeds, Extra extra, Entries entries)
        {
            Log.Write($"Getting '{extra.Name}'");
            var dbPath = Path.Combine(app.Path, extra.Path[0]);
            ValidateFileHashes(new[] { dbPath }, extra.Sha256);

            using var con = DictConnection.Create(dbPath, extra.Type[0], null, app.Name, seeds);

            var nEntries = con.CountEntries();
            Console.Title = $"Processed 0 of {nEntries} entries";

            using var prBar = VtProgressBar.Create(nEntries, "Processed");
            foreach (var (hash, rawContent, idx) in con.ReadEntries().Select((e, i) => (e.hash, e.content, i)))
            {
                var (content, errorMessages) = Markup.Resolve(rawContent);
                doc.LoadHtml(content);

                foreach (var node in doc.DocumentNode.SelectNodes("//br")?.ToArray() ?? Array.Empty<HtmlNode>())
                    node.ParentNode.ReplaceChild(doc.CreateTextNode(Environment.NewLine), node);

                var lines = doc.DocumentNode.InnerText
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Select(e => PunctationRegex.Replace(e, m => {
                        if (m.Value == "  ")
                            return " ";
                        if (m.Value == " ,")
                            return ",";
                        return m.Value;
                    }))
                    .Where(e => e.Length > 0);

                var strBuilder = new StringBuilder($"<{Markups.Meta}>Thesaurus</{Markups.Meta}>");

                entries.TryGetValue(hash, out var entry);
                var word = entry?.Word;

                var thesaurusItems = new List<ThesaurusItem>();
                foreach (var line in lines)
                {
                    if (line.StartsWith(AntonymPrefix))
                    {
                        thesaurusItems.Last().Antonyms.Add(line[AntonymPrefix.Length..]);
                        continue;
                    }

                    var match = HeadlineRegex.Match(line);
                    if (match.Success)
                    {
                        var inferredWord = match.Groups[1].Value;
                        if (word == null)
                            word = inferredWord;
                        thesaurusItems.Add(new ThesaurusItem { HeadLine = line });
                        continue;
                    }
                    else if (line == word)
                    {
                        thesaurusItems.Add(new ThesaurusItem { HeadLine = line });
                        continue;
                    }

                    var item = new ThesaurusItem();
                    item.Synonyms.AddRange(line.Split(", "));
                    thesaurusItems.Add(item);
                }

                foreach (var item in thesaurusItems)
                {
                    if (item.HeadLine != null)
                    {
                        strBuilder.Append($"<{Markups.Definition}>{item.HeadLine}</{Markups.Definition}>");
                    }
                    else
                    {
                        strBuilder.Append($"<{Markups.Example} class=\"{Markups.OneIndent} {Markups.RelatedWords}\">");
                        for (var i = 0; i < item.Synonyms.Count; i++)
                        {
                            strBuilder.Append(
                                $"<a href=\"{Tools.UrlEncodeMinimal(item.Synonyms[i])}\">{item.Synonyms[i]}</a>");
                            if (i + 1 < item.Synonyms.Count)
                                strBuilder.Append(", ");
                        }
                        if (item.Antonyms.Any())
                        {
                            strBuilder.Append($"<br><{Markups.MetaTitle}>Phản nghĩa:</{Markups.MetaTitle}> ");
                            for (var i = 0; i < item.Antonyms.Count; i++)
                            {
                                strBuilder.Append(
                                    $"<a href=\"{Tools.UrlEncodeMinimal(item.Antonyms[i])}\">{item.Antonyms[i]}</a>");
                                if (i + 1 < item.Antonyms.Count)
                                    strBuilder.Append(", ");
                            }
                        }
                        strBuilder.Append($"</{Markups.Example}>");
                    }
                }

                yield return (hash, word, strBuilder.ToString());

                Console.Title = $"Processed {idx + 1} of {nEntries} entries";
                prBar.Tick();
            }
        }

        static IEnumerable<(uint hash, string word, string content)>
            ProcessRelatedWords(App app, Dict dict, Seeds seeds, Extra extra, Entries entries)
        {
            Log.Write($"Getting '{extra.Name}'");
            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);
            ValidateFileHashes(new[] { dbPath }, new[] { dict.Sha256 });

            using var con = DictConnection.Create(dbPath, dict.Type.Value, null, app.Name, seeds);

            var nEntries = con.CountRelateds();
            Console.Title = $"Processed 0 of {nEntries} entries";

            using var prBar = VtProgressBar.Create(nEntries, "Processed");
            foreach (var (hash, rawContent, idx) in con.ReadRelateds().Select((e, i) => (e.hash, e.content, i)))
            {
                var relatedWords = rawContent
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => e.Length > 0)
                    .ToArray();

                var strBuilder = new StringBuilder($"<{Markups.Meta}>Từ liên quan</{Markups.Meta}>");

                strBuilder.Append($"<{Markups.Example} class=\"{Markups.OneIndent} {Markups.RelatedWords}\">");
                for (var i = 0; i < relatedWords.Length; i++)
                {
                    strBuilder.Append($"<a href=\"{Tools.UrlEncodeMinimal(relatedWords[i])}\">{relatedWords[i]}</a>");
                    if (i + 1 < relatedWords.Length)
                        strBuilder.Append(", ");
                }
                strBuilder.Append($"</{Markups.Example}>");

                entries.TryGetValue(hash, out var entry);
                var word = entry?.Word;

                yield return (hash, word, strBuilder.ToString());

                Console.Title = $"Processed {idx + 1} of {nEntries} entries";
                prBar.Tick();
            }
        }

        static IEnumerable<(uint hash, string word, string content)>
            ProcessOccurences(App app, Dict dict, Seeds seeds, Extra extra, Entries entries)
        {
            Log.Write($"Getting '{extra.Name}'");
            var dbSubPaths = extra.Path.Select(e => Path.Combine(app.Path, e)).ToArray();
            ValidateFileHashes(dbSubPaths, extra.Sha256);
            var dbPath = Path.GetDirectoryName(dbSubPaths[0]);

            using var con = DictConnection.Create(dbPath, extra.Type[0], null, app.Name, seeds, extra.FileNameMap);

            var nEntries = con.CountEntries();
            Console.Title = $"Processed 0 of {nEntries} entries";

            using var prBar = VtProgressBar.Create(nEntries, "Processed");
            foreach (var (hash, rawContent, idx) in con.ReadEntries().Select((e, i) => (e.hash, e.content, i)))
            {
                var relatedWords = rawContent
                    .Split("||", StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .Where(e => e.Length > 0)
                    .ToArray();

                var strBuilder = new StringBuilder($"<{Markups.Meta}>Mục từ có xuất hiện</{Markups.Meta}>");

                strBuilder.Append($"<{Markups.Example} class=\"{Markups.OneIndent} {Markups.RelatedWords}\">");
                for (var i = 0; i < relatedWords.Length; i++)
                {
                    strBuilder.Append($"<a href=\"{Tools.UrlEncodeMinimal(relatedWords[i])}\">{relatedWords[i]}</a>");
                    if (i + 1 < relatedWords.Length)
                        strBuilder.Append(", ");
                }
                strBuilder.Append($"</{Markups.Example}>");

                entries.TryGetValue(hash, out var entry);
                var word = entry?.Word;

                yield return (hash, word, strBuilder.ToString());

                Console.Title = $"Processed {idx + 1} of {nEntries} entries";
                prBar.Tick();
            }
        }

        static void ProcessImageDict(App app, Dict dict, Seeds seeds, Extra extra, Entries _)
        {
            Log.Write($"Extracing '{extra.Name}'");
            var dbSubPaths = extra.Path.Select(e => Path.Combine(app.Path, e)).ToArray();
            ValidateFileHashes(dbSubPaths, extra.Sha256);
            var metaDbPath = dbSubPaths[0];
            var dataDbPath = dbSubPaths[1];

            // must be in minified form
            var imageStyle = @"<style>d-image-entry{display:inline-block;margin:8px}d-image-entry>img{height:113px;width:auto;float:left}d-content{display:inline-block;padding:.4em}d-ipa{color:brown}d-ipa>*:not(script){display:inline-block;vertical-align:middle}</style>";

            var outputDirPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() ?? "output";
            outputDirPath = Path.Combine(outputDirPath, "Lạc Việt - " + extra.Name);
            Directory.CreateDirectory(outputDirPath);
            var outputResZipPath = Path.Combine(outputDirPath, "res.zip");

            var imageEntries = new List<ImageEntry>();
            using var reader = new BinaryReader(File.OpenRead(dataDbPath));

            reader.ReadInt32();  // int version
            reader.ReadSingle(); // float version

            var idxTbOffset = reader.ReadInt32();
            reader.BaseStream.Position = idxTbOffset;
            var idxTbSize = reader.ReadInt32();
            var nIdx = reader.ReadInt32();

            for (var i = 0; i < nIdx; i++)
            {
                var entryOffset = reader.ReadInt32();
                var entrySize = reader.ReadInt32();
                var topicId = reader.ReadInt32();
                var imageId = reader.ReadInt32(); // there are something more in imageId but we don't need them
                var enNameSize = reader.ReadInt32();
                var enName = Encoding.UTF8.GetString(reader.ReadBytes(enNameSize));
                var viNameSize = reader.ReadInt32();
                var viName = Encoding.UTF8.GetString(reader.ReadBytes(viNameSize)).Normalize();
                var viNameSize2 = reader.ReadInt32();
                Encoding.UTF8.GetString(reader.ReadBytes(viNameSize2)); // diacritic-free vietnamese name

                imageEntries.Add(new ImageEntry {
                    Id = imageId,
                    TopicId = topicId,
                    EnName = enName,
                    ViName = viName,
                    EntryOffset = entryOffset,
                    EntrySize = entrySize,
                });
            }

            using var con = DictConnection.Create(metaDbPath, extra.Type[0], null, app.Name, seeds);
            var entries = new List<GenericEntry>();

            var nMetaEntries = con.CountImageCatalogGroups();
            Console.Title = $"Queried 0 of {nMetaEntries} entries";

            using var resStream = new ZipArchive(File.Create(outputResZipPath), ZipArchiveMode.Create);
            var imageEntryGroups = imageEntries.GroupBy(e => e.TopicId).ToDictionary(g => g.Key, g => g.ToArray());
            var imageNameCounts = imageEntries.GroupBy(e => e.EnName).ToDictionary(g => g.Key, g => g.Count());
            var menuContentBuilder = new StringBuilder();
            using (var prBar2 = VtProgressBar.Create(nMetaEntries, "Queried"))
            {
                foreach (var (group, idx) in con.ReadImageCatalogGroups().Select((g, i) => (g, i)))
                {
                    var categoryId = group.Key;
                    var categoryName = group.First().CategoryName;
                    var categoryImage = group.First().CategoryImage;

                    var homeHref = Tools.UrlEncodeMinimal(extra.Name);
                    var categoryContentBuilder = new StringBuilder(
                        $"<a href=\"{homeHref}\">&#60; Back to Home page</a><br>");
                    foreach (var topic in group)
                    {
                        var topicId = topic.TopicId;
                        var topicName = topic.TopicName;
                        var topicNameTranslated = topic.TopicNameTranslated.Normalize();
                        var topicImage = topic.TopicImage;

                        var parentCategoryHref = Tools.UrlEncodeMinimal($"Category: {categoryName}");
                        var topicContentBuilder = new StringBuilder(
                            $"<a href=\"{parentCategoryHref}\">&#60; Back to Category: {categoryName}</a>" +
                            $"<h3 class=\"headline\">{topicNameTranslated}</h3>"
                        );
                        foreach (var imageEntry in imageEntryGroups[topicId])
                        {
                            var imageId = imageEntry.Id;
                            reader.BaseStream.Position = imageEntry.EntryOffset;

                            var imageSize = reader.ReadInt32();
                            var image = reader.ReadBytes(imageSize);
                            Tools.DecryptExtraContentInPlace(image);
                            using (var zipEntry = resStream.CreateEntry($"{imageId}.jpg", Optimal).Open())
                                zipEntry.Write(image);

                            var ipaSize = reader.ReadInt32();
                            var ipa = reader.ReadBytes(ipaSize);
                            Tools.DecryptExtraContentInPlace(ipa);
                            var ipaStr = Encoding.UTF8.GetString(ipa);

                            var mp3Size = reader.ReadInt32();
                            var mp3 = reader.ReadBytes(mp3Size);
                            Tools.DecryptExtraContentInPlace(mp3);
                            using (var zipEntry = resStream.CreateEntry($"{imageId}.mp3", Optimal).Open())
                                zipEntry.Write(mp3);

                            var imageName = imageEntry.EnName;
                            var nameCount = imageNameCounts[imageEntry.EnName];
                            if (nameCount > 1)
                            {
                                imageName += new string('\u200B', nameCount - 1);
                                imageNameCounts[imageEntry.EnName] = nameCount - 1;
                            }

                            var parentTopicHref = Tools.UrlEncodeMinimal($"Topic: {topicName}");
                            entries.Add(new GenericEntry {
                                UseStyleRef = false,
                                Type = 'x',
                                Word = imageName,
                                Content =
                                    imageStyle +
                                    $"<a href=\"{parentTopicHref}\">&#60; Back to Topic: {topicName}</a><br/>" +
                                    $"<d-image-entry>" +
                                        $"<img src='{imageId}.jpg'/>" +
                                        $"<d-content>" +
                                            $"{imageEntry.EnName}<br/>" +
                                            $"<d-ipa>" +
                                                $"<span>{ipaStr}</span> " +
                                                $"<rref>{imageId}.mp3</rref>" +
                                            $"</d-ipa><br/>" +
                                            $"{imageEntry.ViName}<br/>" +
                                        $"</d-content>" +
                                    $"</d-image-entry>"
                            });

                            var href = Tools.UrlEncodeMinimal(imageName);
                            topicContentBuilder.Append(
                                $"<a href=\"{href}\">" +
                                    $"<d-image>" +
                                        $"<img src=\"{imageId}.jpg\"/>" +
                                        $"<d-content>{imageEntry.EnName}</d-content>" +
                                    $"</d-image>" +
                                $"</a>"
                            );
                        }

                        using (var zipEntry = resStream.CreateEntry($"topic_{topicId}.jpg", Optimal).Open())
                            zipEntry.Write(topicImage);

                        entries.Add(new GenericEntry {
                            Word = "Topic: " + topicName,
                            Content = topicContentBuilder.ToString(),
                        });

                        var topicHref = Tools.UrlEncodeMinimal($"Topic: {topicName}");
                        categoryContentBuilder.Append(
                            $"<a href=\"{topicHref}\">" +
                                $"<d-topic-image>" +
                                    $"<img src=\"topic_{topicId}.jpg\"/>" +
                                    "<div class=\"pos-mark\"></div>" +
                                    $"<d-content>{topicName}</d-content>" +
                                    "<div class=\"padding-mark\"></div>" +
                                $"</d-topic-image>" +
                            $"</a>"
                        );
                    }

                    using (var zipEntry = resStream.CreateEntry($"category_{categoryId}.jpg", Optimal).Open())
                        zipEntry.Write(categoryImage);

                    entries.Add(new GenericEntry {
                        Word = "Category: " + categoryName,
                        Content = categoryContentBuilder.ToString(),
                    });

                    Console.Title = $"Queried {idx + 1} of {nMetaEntries} entries";
                    prBar2.Tick();

                    var categoryHref = Tools.UrlEncodeMinimal($"Category: {categoryName}");
                    menuContentBuilder.Append(
                        $"<a href=\"{categoryHref}\">" +
                            $"<d-category-image>" +
                                $"<img src=\"category_{categoryId}.jpg\"/>" +
                                $"<d-content>{categoryName}</d-content>" +
                                "<span class=\"padding-mark\"></span>" +
                            $"</d-category-image>" +
                        $"</a>");
                }
            }

            entries.Add(new GenericEntry {
                Word = extra.Name,
                Content = menuContentBuilder.ToString(),
            });

            Console.Title = $"Wrote 0 of {entries.Count} entries";
            using (var prBar = VtProgressBar.Create(entries.Count, "Wrote"))
            {
                Tools.WriteStarDict(outputDirPath, extra.ShortName, extra.Name, extra.StyleSheet,
                    $"'{extra.Name}' được trích xuất từ bộ từ điển Lạc Việt đến hết ngày 26/11/2021, sử dụng keyword '{extra.Name}' để vào trang chính.",
                    entries,
                    sameTypeHtml: false,
                    styleSheetCallback: (fileName, content) => {
                        using var zipEntry = resStream.CreateEntry(fileName, Optimal).Open();
                        zipEntry.Write(Encoding.UTF8.GetBytes(content));
                    },
                    progressCallback: idx => {
                        Console.Title = $"Wrote {idx + 1} of {entries.Count} entries";
                        prBar.Tick();
                    });
            }

            Tools.CopyIcoOrPng(Path.Combine("Icons", extra.ShortName), Path.Combine(outputDirPath, extra.ShortName));
        }

        static void ProcessGrammarDict(App app, Dict dict, Seeds seeds, Extra extra, Entries _)
        {
            Log.Write($"Extracing '{extra.Name}'");

            var dbPath = Path.Combine(app.Path, extra.Path[0]);
            ValidateFileHashes(new[] { dbPath }, extra.Sha256);

            var jsLoader = File.ReadAllText("Scripts/loader.js");

            var outputDirPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() ?? "output";
            outputDirPath = Path.Combine(outputDirPath, "Lạc Việt - " + extra.Name);
            Directory.CreateDirectory(outputDirPath);
            var outputResZipPath = Path.Combine(outputDirPath, "res.zip");
            using var resStream = new ZipArchive(File.Create(outputResZipPath), ZipArchiveMode.Create);

            resStream.CreateEntryFromFile("Scripts/grammar.js", "grammar.js");

            using var con = DictConnection.Create(dbPath, extra.Type[0], null, app.Name, seeds);
            var entries = new List<GenericEntry>();

            var nEntries = con.CountGrammarItems();
            Console.Title = $"Processed 0 of {nEntries} entries";

            var lessonTitleCounts = new Dictionary<string, int>();

            var lastLessonListBuilder = new StringBuilder();
            var lastTopicListBuilder = new StringBuilder();
            var tableOfContentBuilder = new StringBuilder("<ol class=\"level\" type=\"I\">");
            using (var prBar = VtProgressBar.Create(nEntries, "Processed"))
            {
                var i = 0;
                foreach (var grammarItem in con.ReadGrammarItems())
                {
                    if (grammarItem is GrammarLesson grammarLesson)
                    {
                        doc.LoadHtml(grammarLesson.Content);

                        var cssStyle = string.Join("",
                            doc.DocumentNode
                                .SelectNodes("//style")
                                .Select(e => {
                                    var style = e.InnerText.Trim();
                                    if (style.StartsWith("<!--") && style.EndsWith("-->"))
                                        style = style[4..^3];
                                    return style;
                                }));
                        cssStyle = Scss.ConvertToCss("d-content {" + cssStyle + "}", new ScssOptions {
                            OutputStyle = ScssOutputStyle.Compressed,
                        }).Css;
                        using (var zipEntry = resStream.CreateEntry(i + ".css", Optimal).Open())
                            zipEntry.Write(Encoding.UTF8.GetBytes(cssStyle));

                        foreach (var img in doc.DocumentNode.SelectNodes("//img")?.ToArray() ?? Array.Empty<HtmlNode>())
                        {
                            var imgPath = img.GetAttributeValue("src", "not-found.jpg");
                            img.SetAttributeValue("src", Path.GetFileName(imgPath));
                        }
                        var bodyHtml = doc.DocumentNode.SelectNodes("//body").First().InnerHtml;

                        var title = grammarLesson.Title;
                        var modTitle = title;
                        lessonTitleCounts.TryGetValue(title, out var titleCount);
                        lessonTitleCounts[title] = titleCount + 1;
                        if (titleCount > 0)
                            modTitle = title + new string('\u200B', titleCount);
                        var modExerciseTitle = modTitle + " - Exercise";
                        var homeHref = Tools.UrlEncodeMinimal(extra.Name);
                        var exerciseHref = Tools.UrlEncodeMinimal(modExerciseTitle);
                        entries.Add(new GenericEntry {
                            Word = modTitle,
                            Content =
                                $"<link rel=\"stylesheet\" href=\"{i}.css\"/>" +
                                $"<a href=\"{homeHref}\">&#60; Back to Home page</a> | " +
                                    $"<a href=\"{exerciseHref}\">Go to Exercise page &#62;</a><br>" +
                                $"<i><b>Level:</b> {grammarLesson.Level} ({grammarLesson.LevelVi})</i><br>" +
                                $"<i><b>Topic:</b> {grammarLesson.Topic[5..]}</i><br><br>" +
                                $"<d-content>{bodyHtml}</d-content>",
                        });
                        var lessonHref = Tools.UrlEncodeMinimal(modTitle);
                        var exerciseContent = grammarLesson.ExerciseContent;
                        var patched = false;
                        var corruptedEntries = extra.Patches?.CorruptedEntries ?? new Dictionary<string, object>();
                        if (corruptedEntries.TryGetValue(modExerciseTitle, out var solution) == true)
                        {
                            Log.Write($"Fix content for item '{modExerciseTitle}'");
                            if (solution is int len)
                                exerciseContent = exerciseContent[0..len];
                            else if (solution is string altContent)
                                exerciseContent = altContent;
                            patched |= true;
                        }
                        var substitutions = extra.Patches?.Substitutions ?? Array.Empty<Dict._Patches.Substitution>();
                        foreach (var substitution in substitutions)
                        {
                            if (substitution.Words.Contains(modExerciseTitle))
                            {
                                if (!patched) Log.Write($"Fix content for word '{modExerciseTitle}'");
                                for (var ii = 0; ii < substitution.Targets.Length; ii++)
                                {
                                    var target = substitution.Targets[ii];
                                    var replacement = substitution.Replacements[ii];
                                    exerciseContent = exerciseContent.Replace(target, replacement);
                                    patched |= true;
                                }
                            }
                        }
                        var resolvedExerciseContent = Markup.ResolveExercise(exerciseContent);
                        var guid = Guid.NewGuid().ToString();
                        entries.Add(new GenericEntry {
                            Word = modExerciseTitle,
                            Content =
                                $"<div id=\"{guid}\">" +
                                    $"<img style=\"display:none\" src=\"grammar.js\" id=\"{guid}-img\">" +
                                    $"<script data-guid=\"{guid}\">{jsLoader}</script>" +
                                    $"<a href=\"{homeHref}\">&#60; Back to Home page</a><br>" +
                                    $"<a href=\"{lessonHref}\">&#60; Back to Lesson page</a><br>" +
                                    $"<i><b>Level:</b> {grammarLesson.Level} ({grammarLesson.LevelVi})</i><br>" +
                                    $"<i><b>Topic:</b> {grammarLesson.Topic[5..]}</i><br><br>" +
                                    $"<button type=\"button\" class=\"show-answer-btn\">Show answer</button>" +
                                    "<div>" +
                                        resolvedExerciseContent +
                                    "</div>" +
                                    $"<button type=\"button\" class=\"show-answer-btn\">Show answer</button>" +
                                "</div>",
                        });

                        lastLessonListBuilder.Append(
                            $"<li>" +
                                $"<a href=\"{lessonHref}\">{title}</a> (" +
                                $"<a href=\"{exerciseHref}\">Exercise</a>)" +
                            $"</li>"
                        );

                        Console.Title = $"Processed {i++ + 1} of {nEntries} entries";
                        prBar.Tick();
                    }
                    else if (grammarItem is GrammarTopic grammarTopic)
                    {
                        var title = grammarTopic.Title[5..];
                        lastTopicListBuilder
                            .Append($"<li>{title}</li>")
                            .Append($"<ol class=\"lesson\">")
                            .Append(lastLessonListBuilder)
                            .Append("</ol>");
                        lastLessonListBuilder.Clear();
                    }
                    else if (grammarItem is GrammarLevel grammarLevel)
                    {
                        var title = grammarLevel.Title;
                        var titleVi = grammarLevel.TitleVi;
                        tableOfContentBuilder
                            .Append($"<li>{title} ({titleVi})</li>")
                            .Append("<ol class=\"topic\" type=\"1\">")
                            .Append(lastTopicListBuilder)
                            .Append("</ol>");
                        lastTopicListBuilder.Clear();
                    }
                }
            }

            tableOfContentBuilder.Append("</ol>");

            entries.Add(new GenericEntry {
                Word = extra.Name,
                Content = tableOfContentBuilder.ToString(),
            });

            Console.Title = $"Wrote 0 of {entries.Count} entries";
            using (var prBar = VtProgressBar.Create(entries.Count, "Wrote"))
            {
                Tools.WriteStarDict(outputDirPath, extra.ShortName, extra.Name, extra.StyleSheet,
                    $"'{extra.Name}' được trích xuất từ bộ từ điển Lạc Việt đến hết ngày 26/11/2021, sử dụng keyword '{extra.Name}' để vào trang chính.",
                    entries,
                    styleSheetCallback: (fileName, content) => {
                        using var zipEntry = resStream.CreateEntry(fileName, Optimal).Open();
                        zipEntry.Write(Encoding.UTF8.GetBytes(content));
                    },
                    progressCallback: idx => {
                        Console.Title = $"Wrote {idx + 1} of {entries.Count} entries";
                        prBar.Tick();
                    });
            }

            foreach (var filePath in Directory.EnumerateFiles(Path.Combine(app.Path, "images")))
                resStream.CreateEntryFromFile(filePath, Path.GetFileName(filePath));

            Tools.CopyIcoOrPng(Path.Combine("Icons", extra.ShortName), Path.Combine(outputDirPath, extra.ShortName));

            throw new NotImplementedException();
        }
    }
}

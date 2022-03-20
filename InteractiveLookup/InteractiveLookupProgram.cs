using Common;
using Extractor;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace InteractiveLookup
{
    using static Environment;
    using Seeds = Dictionary<uint, string>;

    class InteractiveLookupProgram
    {
        const int OrdinalPadding = 3;

        enum QueryMethod
        {
            GetEntry = 1,
            GetWordByKeyword = 2,
            GetWordByHash = 3,
        }

        static readonly Config config = Config.Get();

        static void Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        SELECT_APP:
            var app = AskAnApplication(config);

        SELECT_DICT:
            var dict = AskADictionary(app);
            if (dict == null)
                goto SELECT_APP;

            ValidateFileSHA256(app, dict);

            try
            {
                LookupEntry(app, dict, config.SeedsByName[app.Name]);
            }
            catch (NotSupportedException e)
            {
                Console.Error.WriteLine("Lỗi: " + e.Message);
                Console.Error.WriteLine();
            }

            goto SELECT_DICT;
        }

        static uint ConsoleReadOrdinal(uint startIndex, uint endIndex)
        {
            uint appNum;
            while (true)
            {
                Console.Write("> ");

                var input = Console.ReadLine().Trim();
                if (input == "`")
                    input = "0";

                if (uint.TryParse(input, out appNum) && appNum >= startIndex && appNum <= endIndex)
                    break;
            }
            return appNum;
        }

        static Config.App AskAnApplication(Config config)
        {
            Console.WriteLine("Chọn bộ từ điển:");

            foreach (var (app, idx) in config.Apps.Select((e, i) => (e, i)))
                Console.WriteLine($"{idx + 1,OrdinalPadding}. {app.Name}");

            var ordinal = ConsoleReadOrdinal(1, (uint)config.Apps.Length);

            Console.WriteLine();

            return config.Apps[ordinal - 1];
        }

        static Config.App.Dict AskADictionary(Config.App app)
        {
            Console.WriteLine($"Chọn từ điển trong {app.Name}:");
            foreach (var (dict, idx) in app.Dicts.Select((e, i) => (e, i)))
                Console.WriteLine($"{idx + 1,OrdinalPadding}. {dict.Name}");
            Console.WriteLine($"{'`',OrdinalPadding}. Trở lại menu vừa rồi");

            var ordinal = ConsoleReadOrdinal(0, (uint)app.Dicts.Length);

            Console.WriteLine();
            return ordinal != 0 ? app.Dicts[ordinal - 1] : null;
        }

        static readonly SHA256 sha256Hasher = SHA256.Create();
        static void ValidateFileSHA256(Config.App app, Config.App.Dict dict)
        {
            Console.WriteLine("Đang kiểm tra hash...");

            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);
            var hash = Convert.ToHexString(
                sha256Hasher.ComputeHash(new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)));

            if (!hash.Equals(dict.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Cơ sở dữ liệu '{dict.Name}' ({dict.Path}) không khớp với mã hash trong file cấu hình. Có thể từ điển đã được cập nhật, hoặc cấu hình không chính xác.");

            Console.WriteLine();
        }

        public static ConsoleColor DefaultColor = Console.ForegroundColor;

        static void LookupEntry(Config.App app, Config.App.Dict dict, Seeds seeds)
        {
            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);

            var keywordEncoding = Encoding.GetEncoding(dict.KeywordEncoding);

            using var con = DictConnection.Create(dbPath, dict.Type.Value, keywordEncoding, app.Name, seeds);

            while (true)
            {
                Console.Write("Bạn muốn tra cứu từ gì (nhập '--' để dừng): ");
                var keyword = Console.ReadLine().Trim();
                if (keyword == "--")
                    break;

                var normalizedKeyword = keywordEncoding.CodePage == 1258
                    ? keyword.ToVietnameseDecomposed()
                    : keyword;

                var hash = Tools.HashKeyword(keywordEncoding.GetBytes(normalizedKeyword));

                var content = con.ReadEntryData(normalizedKeyword, hash);
                if (content == null)
                    continue;

                Patching.PreApply(dict, normalizedKeyword, ref content);
                string[] errorMessages;
                (content, errorMessages) = Markup.Resolve(content);
                if (Patching.PostApply(dict, normalizedKeyword, ref content))
                    errorMessages = null;
                if (errorMessages == null)
                {
                    Console.WriteLine("Entry is corrupted:");
                    foreach (var e in errorMessages)
                        Console.WriteLine("  " + e);
                }
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(normalizedKeyword.Normalize(NormalizationForm.FormC) + $" ({hash})");
                Console.ForegroundColor = DefaultColor;
                foreach (var item in ParseAndFormat(config, content))
                {
                    if (item is ConsoleColor color)
                        Console.ForegroundColor = color;
                    else
                        Console.Write(item.ToString().Normalize(NormalizationForm.FormC));
                }
                Console.WriteLine();
            }

            Console.WriteLine();
        }

        public static IEnumerable<object> ParseAndFormat(Config config, string content)
        {
            var markups = config.ConfigMarkup;

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            static bool IsElement(HtmlNode node, string name)
                => node.NodeType == HtmlNodeType.Element && node.Name == name;

            static IEnumerable<object> ResolveText(HtmlNode node, ConsoleColor defaultColor)
            {
                foreach (var subNode in node.ChildNodes)
                {
                    if (IsElement(subNode, "a"))
                    {
                        yield return ConsoleColor.Red;
                        yield return WebUtility.HtmlDecode(subNode.InnerText);
                        yield return defaultColor;
                    }
                    else if (IsElement(subNode, "br"))
                    {
                        yield return NewLine;
                    }
                    else if (IsElement(subNode, "p"))
                    {
                        yield return WebUtility.HtmlDecode(subNode.InnerText);
                        yield return subNode.InnerText[^1] != NewLine[^1] ? NewLine : "";
                    }
                    else if (IsElement(subNode, "div"))
                    {
                        foreach (var item in ResolveText(subNode, defaultColor))
                            yield return item;
                        yield return subNode.InnerText[^1] != NewLine[^1] ? NewLine : "";
                    }
                    else
                    {
                        yield return WebUtility.HtmlDecode(subNode.InnerText);
                    }
                }
            }

            foreach (var node in doc.DocumentNode.ChildNodes)
            {
                if (IsElement(node, markups.Meta))
                {
                    yield return NewLine;
                    yield return ConsoleColor.Cyan;
                    yield return "ℹ️  ";
                    foreach (var item in ResolveText(node, ConsoleColor.Cyan))
                        yield return item;
                }
                else if (IsElement(node, markups.Definition))
                {
                    yield return NewLine;
                    yield return ConsoleColor.Green;
                    yield return "  🟩";
                    foreach (var item in ResolveText(node, ConsoleColor.Green))
                        yield return item;
                }
                else if (IsElement(node, markups.Example))
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    yield return "      ";
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
                else if (IsElement(node, markups.ExampleTranslation))
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    yield return "      ";
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
                else if (IsElement(node, markups.PhoneticNotation))
                {
                    yield return NewLine;
                    yield return ConsoleColor.Red;
                    yield return "🔊[";
                    foreach (var item in ResolveText(node, ConsoleColor.Red))
                        yield return item;
                    yield return "]";
                }
                else if (IsElement(node, markups.Idiom))
                {
                    yield return NewLine;
                    yield return ConsoleColor.Green;
                    yield return "  🟩";
                    foreach (var item in ResolveText(node, ConsoleColor.Green))
                        yield return item;
                }
                else if (IsElement(node, markups.IdiomTranslation))
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    yield return "   ";
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
                else if (IsElement(node, markups.IdiomExample))
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    yield return "      ";
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
                else if (IsElement(node, markups.IdiomExampleTranslation))
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    yield return "      ";
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
                else if (IsElement(node, markups.Meta2))
                {
                    yield return NewLine;
                    yield return ConsoleColor.Cyan;
                    yield return "ℹ️  ";
                    foreach (var item in ResolveText(node, ConsoleColor.Cyan))
                        yield return item;
                }
                else if (IsElement(node, markups.Alternative))
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
                else if (IsElement(node, markups.Media))
                {
                }
                else
                {
                    yield return NewLine;
                    yield return DefaultColor;
                    foreach (var item in ResolveText(node, DefaultColor))
                        yield return item;
                }
            }

            yield return DefaultColor;
            yield return NewLine;
        }
    }
}

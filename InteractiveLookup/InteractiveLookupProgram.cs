using Common;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace InteractiveLookup
{
    using static BinaryPrimitives;
    using static EscColor;
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

        static void Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var config = Config.Get();
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
                goto SELECT_DICT;
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

        static void LookupEntry(Config.App app, Config.App.Dict dict, Seeds seeds)
        {
            var dbPath = Path.Combine(app.Path, app.DictPath, dict.Path);

            var keywordEncoding = Encoding.GetEncoding(dict.KeywordEncoding);

            using var con = DictConnection.Create(dbPath, dict.Type.Value, keywordEncoding, app.Name, seeds);

            while (true)
            {
                Console.Write("Bạn muốn tra cứu từ gì (nhập '-' để dừng): ");
                var keyword = Console.ReadLine().Trim();
                if (keyword == "-")
                    break;

                var normalizedKeyword = keywordEncoding.CodePage == 1258
                    ? keyword.ToVietnameseDecomposed()
                    : keyword;

                var hash = Helper.HashKeyword(keywordEncoding.GetBytes(normalizedKeyword));

                var content = con.ReadEntryData(normalizedKeyword, hash);
                if (content != null)
                {
                    string[] errorMessages;
                    (content, errorMessages) = Helper.ResolveLacVietMarkups(content);
                    if (errorMessages == null)
                        Console.WriteLine("Entry is corrupted.");
                    var lenBefore = content.Length;
                    content = Helper.TruncateGarbage(content);
                    var lenAfter = content.Length;
                    content = ParseAndFormat(content);
                    Console.WriteLine();
                    if (lenBefore != lenAfter)
                        Console.WriteLine(BrightYellow + keyword + $" ({hash}, garbage trimmed)" + Reset);
                    else
                        Console.WriteLine(BrightYellow + keyword + $" ({hash})" + Reset);
                    Console.WriteLine(content);
                }
            }
            Console.WriteLine();
        }
    }

    static class EscColor
    {
        public static string Reset = "\u001b[0m";

        public static string Black = Reset + "\u001b[30m";
        public static string Red = Reset + "\u001b[31m";
        public static string Green = Reset + "\u001b[32m";
        public static string Yellow = Reset + "\u001b[33m";
        public static string Blue = Reset + "\u001b[34m";
        public static string Magenta = Reset + "\u001b[35m";
        public static string Cyan = Reset + "\u001b[36m";
        public static string White = Reset + "\u001b[37m";

        public static string BrightBlack = Reset + "\u001b[30;1m";
        public static string BrightRed = Reset + "\u001b[31;1m";
        public static string BrightGreen = Reset + "\u001b[32;1m";
        public static string BrightYellow = Reset + "\u001b[33;1m";
        public static string BrightBlue = Reset + "\u001b[34;1m";
        public static string BrightMagenta = Reset + "\u001b[35;1m";
        public static string BrightCyan = Reset + "\u001b[36;1m";
        public static string BrightWhite = Reset + "\u001b[37;1m";

        static readonly string NewLine = Environment.NewLine;
        static readonly Regex Markups =
            new(@"%%|\*\*|``|\#\#|~~|\[\[|\]\]|\$\$|:|^.|\n\$\$|<eof>", RegexOptions.Compiled);

        public static string ParseAndFormat(string content)
        {
            var formatedContent = new StringBuilder(content.Length);
            var lastIdx = 0;
            string MakeNewLine() => lastIdx != 0 ? NewLine : "";
            string Join(params string[] values) => string.Join("", values);

            var lastIndentation = "";
            var lastColor = Reset;
            var insideRed = false;
            var lastMatch = default(string);

            content = Markups.Replace(content, match => {
                var replacement = "";
                switch (match.Value)
                {
                    case "<eof>":
                        replacement = Reset;
                        break;
                    case "%%":
                        replacement = Join(MakeNewLine(), lastIndentation = "  ", lastColor = Cyan);
                        break;
                    case ":":
                        replacement = ":";
                        if (lastMatch == "%%")
                            replacement += Reset;
                        break;
                    case "**":
                        replacement = Join(MakeNewLine(), "ℹ️", lastColor = BrightCyan);
                        lastIndentation = "  ";
                        break;
                    case "``":
                        replacement = Join(MakeNewLine(), "  🟩", lastColor = BrightGreen);
                        lastIndentation = "    ";
                        break;
                    case "##":
                        replacement = Join(MakeNewLine(), lastIndentation = "      ", lastColor = Reset);
                        break;
                    case "~~":
                        replacement = MakeNewLine() + lastIndentation;
                        break;
                    case "[[":
                        replacement = Join(MakeNewLine(), "🔊", lastColor = BrightRed, "[");
                        lastIndentation = "  ";
                        break;
                    case "]]":
                        replacement = "]" + (lastColor = Reset);
                        break;
                    case "$$":
                        if (insideRed)
                        {
                            replacement = lastColor;
                            insideRed = false;
                        }
                        else
                        {
                            insideRed = true;
                            replacement = Red;
                        }
                        break;
                    case "\n$$":
                        insideRed = true;
                        replacement = Join("\n", lastIndentation, Red);
                        break;
                    default:
                        if (match.Value.Length == 1)
                            replacement = lastIndentation + match.Value;
                        else throw new Exception();
                        break;
                }
                lastIdx = match.Index + match.Value.Length;
                lastMatch = match.Value;
                return replacement;
            });
            return content;
        }
    }
}

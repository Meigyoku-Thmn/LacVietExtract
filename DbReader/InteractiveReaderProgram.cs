using Common;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static DbReader.EscColor;

namespace DbReader
{
    class InteractiveReaderProgram
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Initialize();
        MAIN_MENU:
            Console.WriteLine();
            Console.WriteLine("Hãy chọn bộ từ điển trong phần mềm Lạc Việt mtd10 CVH:");
            Console.WriteLine("   1. Từ điển Trung-Việt");
            Console.WriteLine("   2. Từ điển Việt-Trung");
            Console.WriteLine("   3. Từ điển Tiếng Việt");
            Console.WriteLine("   4, -. Tắt");
        MAIN_MENU_GET_INPUT:
            Console.Write("> ");
            var input = Console.ReadLine().Trim();
            if (input.Length == 0)
                goto MAIN_MENU_GET_INPUT;
            if (input[0] == '-' || input[0] == '4')
                return;
            if (input[0] < '1' || input[0] > '4')
                goto MAIN_MENU_GET_INPUT;
            var dictIdx = int.Parse(input[0..1]);

        SUB_MENU:
            Console.WriteLine();
            Console.WriteLine("Bạn muốn làm gì:");
            Console.WriteLine("   1. Tra cứu từ điển");
            Console.WriteLine("   2. Tra cứu mlob (dùng keyword)");
            Console.WriteLine("   3. Tra cứu mlob (dùng giá trị hash)");
            Console.WriteLine("   4, -. Quay lại");
        SUB_MENU_GET_INPUT:
            Console.Write("> ");
            input = Console.ReadLine().Trim();
            if (input.Length == 0)
                goto MAIN_MENU;
            if (input[0] == '-' || input[0] == '4')
                goto MAIN_MENU;
            if (input[0] == '1')
                LookupDictionaries(dictIdx);
            else if (input[0] == '2')
                Lookup_mlob(dictIdx, usingKeyword: true);
            else if (input[0] == '3')
                Lookup_mlob(dictIdx, usingKeyword: false);
            else
                goto SUB_MENU_GET_INPUT;
            goto SUB_MENU;
        }

        static readonly string CVH_Chi_Vie = "CVH_Chi_Vie.db";
        static readonly string CVH_Vie_Chi = "CVH_Vie_Chi.db";
        static readonly string CVH_Vie_Vie = "CVH_Vie_Vie.db";

        static void Initialize()
        {
            var hasChiVie = File.Exists(CVH_Chi_Vie);
            var hasVieChi = File.Exists(CVH_Vie_Chi);
            var hasVieVie = File.Exists(CVH_Vie_Vie);

            if (!hasChiVie || !hasVieChi || !hasVieVie)
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length < 2)
                    throw new ArgumentException();
                var lacvietPath = args[1];
                var dataPath = lacvietPath + "\\DATA";
                var copiedFileName = new List<string>();

                if (!hasChiVie)
                {
                    File.Copy(Path.Combine(dataPath, "LVCNVN10.DIT"), CVH_Chi_Vie);
                    copiedFileName.Add(CVH_Chi_Vie);
                }
                if (!hasVieChi)
                {
                    File.Copy(Path.Combine(dataPath, "LVVNCN10.DIT"), CVH_Vie_Chi);
                    copiedFileName.Add(CVH_Vie_Chi);
                }
                if (!hasVieVie)
                {
                    File.Copy(Path.Combine(dataPath, "LvVNVN08.dit"), CVH_Vie_Vie);
                    copiedFileName.Add(CVH_Vie_Vie);
                }

                foreach (var fileName in copiedFileName)
                {
                    using var file = new FileStream(fileName, FileMode.Open, FileAccess.Write);
                    file.Write(Encoding.ASCII.GetBytes("SQLite format 3"));
                }
            }
        }

        static string GetDatabasePath(int dictIdx)
        {
            return dictIdx switch {
                1 => $@"Data Source={CVH_Chi_Vie}",
                2 => $@"Data Source={CVH_Vie_Chi}",
                3 => $@"Data Source={CVH_Vie_Vie}",
                _ => throw new Exception("Chưa chỉ định đường dẫn đến thư mục cài đặt Lạc Việt mtd10 CVH."),
            };
        }

        static void Lookup_mlob(int dictIdx, bool usingKeyword)
        {
            var dbPath = GetDatabasePath(dictIdx);

            var entryEncoding = dictIdx == 1
                ? Encoding.GetEncoding(936)
                : Encoding.GetEncoding(1258);

            using var con = new SQLiteConnection(dbPath);
            con.Open();

            while (true)
            {
                Console.WriteLine();
                uint hash;
                if (usingKeyword)
                {
                    Console.Write("Bạn muốn tra cứu bằng keyword gì (nhập '-' để dừng): ");
                    var keyword = Console.ReadLine().Trim();
                    if (keyword == "-")
                        break;

                    hash = Tools.HashKeyword(Encoding.Latin1.GetBytes(keyword));
                }
                else
                {
                    Console.Write("Bạn muốn tra cứu bằng id gì (nhập '-' để dừng): ");
                    var id = Console.ReadLine().Trim();
                    if (id == "-")
                        break;
                    try
                    {
                        hash = uint.Parse(id);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine(e.Message);
                        continue;
                    }
                }

                var query = @$"
                    SELECT
                        mlob.cd     AS cd,
                        mblklen.cd  AS len
                    FROM mlob
                    JOIN mblklen ON mlob.ab = mblklen.ab
                    WHERE mlob.ab = {hash}
                ";
                using var cmd = new SQLiteCommand(query, con);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var data = reader["cd"] as byte[];
                    var encryptedSize = (int)(uint)reader["len"];
                    Tools.DecodeBinaryInPlace(data);
                    try
                    {
                        Tools.DecryptBinaryInPlace(data.AsSpan(0, encryptedSize));
                    }
                    catch (ArgumentException e)
                    {
                        Console.Error.WriteLine(e.Message);
                        continue;
                    }
                    var content = entryEncoding
                        .GetString(data.AsSpan(4, encryptedSize - 4))
                        .Normalize();
                    foreach (var word in content.Split('\0'))
                        Console.WriteLine(word);
                }
            }
        }

        static void LookupDictionaries(int dictIdx)
        {
            var dbPath = GetDatabasePath(dictIdx);

            var entryEncoding = dictIdx == 1
                ? Encoding.GetEncoding(936)
                : Encoding.GetEncoding(1258);

            using var con = new SQLiteConnection(dbPath);
            con.Open();

            while (true)
            {
                Console.WriteLine();
                Console.Write("Bạn muốn tra cứu từ gì (nhập '-' để dừng): ");
                var keyword = Console.ReadLine().Trim();
                if (keyword == "-")
                    break;
                var normalizedKeyword = keyword.ToVietnameseDecomposed();

                var hash = Tools.HashKeyword(entryEncoding.GetBytes(normalizedKeyword));

                var query = @$"
                    SELECT
                        mabcdef.cd      AS cd,
                        mabcdeflen.cd   AS len
                    FROM mabcdef
                    JOIN mabcdeflen ON mabcdef.ab = mabcdeflen.ab
                    WHERE mabcdef.ab = {hash}
                ";
                using var cmd = new SQLiteCommand(query, con);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var data = reader["cd"] as byte[];
                    var encryptedSize = (int)(uint)reader["len"];
                    Tools.DecodeBinaryInPlace(data);
                    bool decrypted;
                    try
                    {
                        decrypted = Tools.DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), allowSkip: true);
                    }
                    catch (ArgumentException e)
                    {
                        Console.Error.WriteLine(e.Message);
                        continue;
                    }
                    var content = decrypted
                        ? Encoding.Latin1.GetString(data.AsSpan(4, encryptedSize - 4))
                        : Encoding.Latin1.GetString(data.AsSpan(0, encryptedSize));
                    var lenBefore = content.Length;
                    content = Tools.ReduceGarbage(content);
                    var lenAfter = content.Length;
                    content = Tools.ResolveLacVietMarkups(content);
                    content = ParseAndFormat(content);
                    Console.WriteLine();
                    if (lenBefore != lenAfter)
                        Console.WriteLine(BrightYellow + keyword + $" ({hash}, garbage trimmed)" + Reset);
                    else
                        Console.WriteLine(BrightYellow + keyword + $" ({hash})" + Reset);
                    Console.WriteLine(content);
                }
            }
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

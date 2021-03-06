using Ionic.Zlib;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Common
{
    using Seeds = Dictionary<uint, string>;

    public static class Tools
    {
        static readonly Config config = Config.Get();
        static readonly Regex ReservedCharRegex = new(@":|\/|\?|#|\[|]|@|!|\$|&|'|\(|\)|\*|\+|,|;|=|<|>", RegexOptions.Compiled);
        public static string UrlEncodeMinimal(string value)
            => ReservedCharRegex.Replace(value, m => $"%{(int)m.Value[0]:X2}");

        public static bool CopyIfNewer(string sourcePath, string destPath)
        {
            DateTime sourceModifiedTime;
            DateTime destModifiedTime;
            var isDir = Directory.Exists(sourcePath);
            if (!isDir)
            {
                sourceModifiedTime = File.GetLastWriteTimeUtc(sourcePath);
                destModifiedTime = File.GetLastWriteTimeUtc(destPath);
            }
            else
            {
                sourceModifiedTime = Directory.EnumerateFiles(sourcePath)
                    .Select(path => File.GetLastWriteTimeUtc(path))
                    .Max();
                destModifiedTime = Directory.EnumerateFiles(destPath)
                    .Select(path => File.GetLastWriteTimeUtc(path))
                    .Max();
            }

            if (sourceModifiedTime <= destModifiedTime)
                return false;

            if (!isDir)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                File.Copy(sourcePath, destPath, true);
            }
            else
            {
                Directory.CreateDirectory(destPath);
                foreach (var filePath in Directory.EnumerateFiles(sourcePath))
                    File.Copy(filePath, Path.Combine(destPath, Path.GetFileName(filePath)), true);
            }
            return true;
        }

        public static bool CopyIcoOrPng(string sourcePath, string destPath)
        {
            if (File.Exists(sourcePath + ".png"))
            {
                sourcePath += ".png";
                destPath += ".png";
            }
            else if (File.Exists(sourcePath + ".ico"))
            {
                sourcePath += ".ico";
                destPath += ".ico";
            }
            else
                return false;
            File.Copy(Path.Combine(sourcePath), Path.Combine(destPath), true);
            return true;
        }

        public static string RemoveDiacritics(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                    stringBuilder.Append(c);
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static void RenameByFileNameMap(string targetPath, Dictionary<string, string> fileNameMap)
        {
            IEnumerable<string> filePaths;
            if (Directory.Exists(targetPath))
                filePaths = Directory.EnumerateFiles(targetPath);
            else
                filePaths = new[] { targetPath };

            foreach (var filePath in filePaths)
                if (fileNameMap.TryGetValue(Path.GetFileName(filePath), out var newFileName))
                    File.Move(filePath, Path.Combine(Path.GetDirectoryName(filePath), newFileName));
        }

        public static string ReplaceInvalidChars(this string content, char replace = '_')
        {
            if (string.IsNullOrEmpty(content))
                return content;

            var invalidChars = Path.GetInvalidFileNameChars();

            var idx = content.IndexOfAny(invalidChars);
            if (idx >= 0)
            {
                var sb = new StringBuilder(content);
                while (idx >= 0)
                {
                    sb[idx] = replace;
                    idx = content.IndexOfAny(invalidChars, idx + 1);
                }
                return sb.ToString();
            }
            return content;
        }

        // circumflex, breve, horn
        static readonly char[] VietnameseDiacritics = new[] {
            "̆ ", "̂ ", "̛ ",
        }.Select(e => e.Trim()[0]).ToArray();
        // no need to process 'i' and 'y'
        static readonly char[] VietnameseBaseVowels = new[] {
            'a', 'e', 'o', 'u',
        };
        public static string ToVietnameseDecomposed(this string text)
        {
            var chars = text.Normalize(NormalizationForm.FormD).Select(e => e as char?).ToArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var chr = chars[i];
                if (!VietnameseDiacritics.Where(d => d == chr).Any())
                    continue;
                if (i - 1 < 0)
                    continue;
                if (VietnameseBaseVowels.Where(v => v == char.ToLower(chars[i - 1].Value)).Any())
                {
                    var precomposedVowel = string.Join("", chars[i - 1].Value, chr).Normalize();
                    if (precomposedVowel.Length != 1)
                        continue;
                    chars[i - 1] = precomposedVowel[0];
                    chars[i] = null;
                    continue;
                }
                if (i - 2 < 0)
                    continue;
                if (VietnameseBaseVowels.Where(v => v == char.ToLower(chars[i - 2].Value)).Any())
                {
                    var precomposedVowel = string.Join("", chars[i - 2].Value, chr).Normalize();
                    if (precomposedVowel.Length != 1)
                        continue;
                    chars[i - 2] = precomposedVowel[0];
                    chars[i] = null;
                    continue;
                }
            }
            return string.Join("", chars.Where(chr => chr != null));
        }

        public static int DecodeBinaryInPlace(byte[] data)
        {
            if (data.Length < 2)
                throw new Exception("Input byte array has length less than 2.");
            var key = data.First();
            var outIdx = 0;
            for (var i = 1; i < data.Length; i++)
            {
                var inputByte = data[i];
                if (inputByte == 0)
                    return outIdx;
                if (inputByte == 1)
                {
                    if (i == data.Length - 1)
                        throw new Exception("Invalid escape sequence in input array.");
                    var inputByte2 = data[++i];
                    if (inputByte2 == 1)
                        inputByte = 0;
                    else if (inputByte2 == 2)
                        inputByte = 1;
                    else if (inputByte2 == 3)
                        inputByte = 39;
                    else
                        throw new Exception("Invalid escape sequence in input array.");
                }

                data[outIdx++] = (byte)(inputByte + key);

            }
            return outIdx;
        }

        public static bool DecryptBinaryInPlace(Span<byte> data, Seeds seeds, bool allowSkip = false)
        {
            var magicCode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4));

            if (!seeds.TryGetValue(magicCode, out var seed) && !allowSkip)
                throw new ArgumentException("Unknown binary type.");
            if (seed == null)
                return false;

            var cipherKey = new Enigma(seed);
            var idx = 4;
            while (idx < data.Length)
                data[idx++] ^= cipherKey.Advance();

            return true;
        }

        static readonly uint[] HashKey = new uint[] {
            0x0, 0x77073096, 0x0EE0E612C, 0x990951BA, 0x76DC419,
            0x706AF48F, 0x0E963A535, 0x9E6495A3, 0x0EDB8832,
            0x79DCB8A4, 0x0E0D5E91E, 0x97D2D988, 0x9B64C2B,
            0x7EB17CBD, 0x0E7B82D07, 0x90BF1D91, 0x1DB71064,
            0x6AB020F2, 0x0F3B97148, 0x84BE41DE, 0x1ADAD47D,
            0x6DDDE4EB, 0x0F4D4B551, 0x83D385C7, 0x136C9856,
            0x646BA8C0, 0x0FD62F97A, 0x8A65C9EC, 0x14015C4F,
            0x63066CD9, 0x0FA0F3D63, 0x8D080DF5, 0x3B6E20C8,
            0x4C69105E, 0x0D56041E4, 0x0A2677172, 0x3C03E4D1,
            0x4B04D447, 0x0D20D85FD, 0x0A50AB56B, 0x35B5A8FA,
            0x42B2986C, 0x0DBBBC9D6, 0x0ACBCF940, 0x32D86CE3,
            0x45DF5C75, 0x0DCD60DCF, 0x0ABD13D59, 0x26D930AC,
            0x51DE003A, 0x0C8D75180, 0x0BFD06116, 0x21B4F4B5,
            0x56B3C423, 0x0CFBA9599, 0x0B8BDA50F, 0x2802B89E,
            0x5F058808, 0x0C60CD9B2, 0x0B10BE924, 0x2F6F7C87,
            0x58684C11, 0x0C1611DAB, 0x0B6662D3D, 0x76DC4190,
            0x1DB7106, 0x98D220BC, 0x0EFD5102A, 0x71B18589,
            0x6B6B51F, 0x9FBFE4A5, 0x0E8B8D433, 0x7807C9A2,
            0x0F00F934, 0x9609A88E, 0x0E10E9818, 0x7F6A0DBB,
            0x86D3D2D, 0x91646C97, 0x0E6635C01, 0x6B6B51F4,
            0x1C6C6162, 0x856530D8, 0x0F262004E, 0x6C0695ED,
            0x1B01A57B, 0x8208F4C1, 0x0F50FC457, 0x65B0D9C6,
            0x12B7E950, 0x8BBEB8EA, 0x0FCB9887C, 0x62DD1DDF,
            0x15DA2D49, 0x8CD37CF3, 0x0FBD44C65, 0x4DB26158,
            0x3AB551CE, 0x0A3BC0074, 0x0D4BB30E2, 0x4ADFA541,
            0x3DD895D7, 0x0A4D1C46D, 0x0D3D6F4FB, 0x4369E96A,
            0x346ED9FC, 0x0AD678846, 0x0DA60B8D0, 0x44042D73,
            0x33031DE5, 0x0AA0A4C5F, 0x0DD0D7CC9, 0x5005713C,
            0x270241AA, 0x0BE0B1010, 0x0C90C2086, 0x5768B525,
            0x206F85B3, 0x0B966D409, 0x0CE61E49F, 0x5EDEF90E,
            0x29D9C998, 0x0B0D09822, 0x0C7D7A8B4, 0x59B33D17,
            0x2EB40D81, 0x0B7BD5C3B, 0x0C0BA6CAD, 0x0EDB88320,
            0x9ABFB3B6, 0x3B6E20C, 0x74B1D29A, 0x0EAD54739,
            0x9DD277AF, 0x4DB2615, 0x73DC1683, 0x0E3630B12,
            0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8, 0x0E40ECF0B,
            0x9309FF9D, 0x0A00AE27, 0x7D079EB1, 0x0F00F9344,
            0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0x0F762575D,
            0x806567CB, 0x196C3671, 0x6E6B06E7, 0x0FED41B76,
            0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC, 0x0F9B9DF6F,
            0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5, 0x0D6D6A3E8,
            0x0A1D1937E, 0x38D8C2C4, 0x4FDFF252, 0x0D1BB67F1,
            0x0A6BC5767, 0x3FB506DD, 0x48B2364B, 0x0D80D2BDA,
            0x0AF0A1B4C, 0x36034AF6, 0x41047A60, 0x0DF60EFC3,
            0x0A867DF55, 0x316E8EEF, 0x4669BE79, 0x0CB61B38C,
            0x0BC66831A, 0x256FD2A0, 0x5268E236, 0x0CC0C7795,
            0x0BB0B4703, 0x220216B9, 0x5505262F, 0x0C5BA3BBE,
            0x0B2BD0B28, 0x2BB45A92, 0x5CB36A04, 0x0C2D7FFA7,
            0x0B5D0CF31, 0x2CD99E8B, 0x5BDEAE1D, 0x9B64C2B0,
            0x0EC63F226, 0x756AA39C, 0x26D930A, 0x9C0906A9,
            0x0EB0E363F, 0x72076785, 0x5005713, 0x95BF4A82,
            0x0E2B87A14, 0x7BB12BAE, 0x0CB61B38, 0x92D28E9B,
            0x0E5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21, 0x86D3D2D4,
            0x0F1D4E242, 0x68DDB3F8, 0x1FDA836E, 0x81BE16CD,
            0x0F6B9265B, 0x6FB077E1, 0x18B74777, 0x88085AE6,
            0x0FF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF,
            0x0F862AE69, 0x616BFFD3, 0x166CCF45, 0x0A00AE278,
            0x0D70DD2EE, 0x4E048354, 0x3903B3C2, 0x0A7672661,
            0x0D06016F7, 0x4969474D, 0x3E6E77DB, 0x0AED16A4A,
            0x0D9D65ADC, 0x40DF0B66, 0x37D83BF0, 0x0A9BCAE53,
            0x0DEBB9EC5, 0x47B2CF7F, 0x30B5FFE9, 0x0BDBDF21C,
            0x0CABAC28A, 0x53B39330, 0x24B4A3A6, 0x0BAD03605,
            0x0CDD70693, 0x54DE5729, 0x23D967BF, 0x0B3667A2E,
            0x0C4614AB8, 0x5D681B02, 0x2A6F2B94, 0x0B40BBE37,
            0x0C30C8EA1, 0x5A05DF1B, 0x2D02EF8D,
        };
        public static uint HashKeyword(byte[] keyword)
        {
            uint hashCode = 0xFFFFFFFF;
            foreach (var e in keyword)
                hashCode = HashKey[(byte)(hashCode ^ e)] ^ (hashCode >> 8);
            return ~hashCode;
        }

        static readonly byte[] ExtraContentKey = new byte[] {
            0xF9, 0xAC, 0xEB, 0xF3, 0xEB, 0xEF, 0xF3
        };
        public static void DecryptExtraContentInPlace(Span<byte> data)
        {
            var size = data.Length;
            var len1 = 8;
            if (size <= 8)
                len1 = size;
            var len2 = 13;
            if (size <= 13)
                len2 = size;
            var len3 = 507;
            if (size <= 507)
                len3 = size;
            var len4 = 1023;
            if (size <= 1023)
                len4 = size;
            var len5 = 3057;
            if (size <= 3057)
                len5 = size;
            var len6 = 4073;
            if (size <= 4073)
                len6 = size;
            var len7 = 5043;
            if (size <= 5043)
                len7 = size;
            var keyIdx = size % 7;
            var i = 0;
            for (; i < len1; ++i)
                data[i] ^= ExtraContentKey[keyIdx];
            var isOddIdx = keyIdx % 2;
            if (isOddIdx != 0)
                for (; i < len2; ++i)
                    data[i] ^= (byte)0x8Cu;
            else
                for (; i < len2; ++i)
                    data[i] ^= (byte)0xF9u;
            for (; i < len3; ++i)
                data[i] ^= (byte)0xACu;
            for (; i < len4; ++i)
                data[i] ^= (byte)0xEBu;
            for (; i < len5; ++i)
                data[i] ^= (byte)0xF3u;
            for (; i < len6; ++i)
                data[i] ^= (byte)0xEBu;
            for (; i < len7; ++i)
                data[i] ^= (byte)0xEFu;
            if (isOddIdx != 0)
            {
                for (; i < size; ++i)
                    data[i] ^= (byte)0xF3u;
                return;
            }
            if (i >= size)
                return;
            do
                data[i++] ^= (byte)0x8Cu;
            while (i < size);
        }

        #region StartDict configuration
        static readonly PropertyInfo[] MarkupKeywords = config.ConfigMarkup.GetType().GetProperties().ToArray();
        static readonly Regex MarkupRegex = new(
            string.Join('|', MarkupKeywords.Select(p => p.Name).OrderByDescending(e => e.Length)),
            RegexOptions.Compiled);
        static readonly Dictionary<string, object> MarkupMap =
            new(MarkupKeywords.Select(p => KeyValuePair.Create(p.Name, p.GetValue(config.ConfigMarkup))));
        static readonly Dictionary<string, string> CssPool = new();
        const string OutputCssFileName = "style.css";
        static readonly byte[] StyleRefBin = Encoding.UTF8.GetBytes(
            $"<link rel=\"stylesheet\" href=\"{OutputCssFileName}\"/>"
        );
        static readonly Regex ControlCharRegex = new(@"[\x00-\x1F]", RegexOptions.Compiled);
        #endregion
        public static void WriteStarDict(string outputDirPath, string fileNameBase,
            string dictName, string styleSheetFileName, string description, IEnumerable<IEntry> entries,
            bool sameTypeHtml = true,
            Action<int> progressCallback = null, Action<string, string> styleSheetCallback = null)
        {
            var dictFileName = fileNameBase + ".dict";
            var outputDictPath = Path.Combine(outputDirPath, dictFileName);
            var outputIdxPath = Path.Combine(outputDirPath, fileNameBase + ".idx.gz");

            int nWord = 0;
            long idxFileSize = 0;
            using (var dictStream = new FileStream(outputDictPath, FileMode.Create, FileAccess.Write))
            using (var indexStream = new GZipStream(new FileStream(outputIdxPath, FileMode.Create, FileAccess.Write),
                CompressionMode.Compress, CompressionLevel.BestCompression))
            {
                var buffer = new byte[sizeof(int)];
                var mappedSortedEntries = entries.Select((e, i) => {
                    var word = ControlCharRegex.Replace(e.Word, "").Trim().Normalize();
                    var content = e.Content.Trim();
                    return new {
                        idx = i,
                        UseStyleRef = e.UseStyleRef,
                        Type = e.Type,
                        WordBin = Encoding.UTF8.GetBytes(word),
                        ContentBin = Encoding.UTF8.GetBytes(content),
                    };
                }).OrderBy(e => e.WordBin, BinArrayComparer.StarDict);

                foreach (var entry in mappedSortedEntries)
                {
                    indexStream.Write(entry.WordBin);
                    indexStream.Write(new byte[] { 0 });

                    // write position
                    BinaryPrimitives.WriteInt32BigEndian(buffer, (int)dictStream.Position);
                    indexStream.Write(buffer);

                    var size = 0;
                    // write size
                    if (entry.UseStyleRef)
                        size = StyleRefBin.Length + entry.ContentBin.Length;
                    else
                        size = entry.ContentBin.Length;
                    if (!sameTypeHtml)
                        size += 1 + 1;
                    BinaryPrimitives.WriteInt32BigEndian(buffer, size);
                    indexStream.Write(buffer);

                    idxFileSize += entry.WordBin.Length + 1 + 4 + 4;

                    if (!sameTypeHtml)
                        dictStream.Write(new[] { (byte)entry.Type });
                    if (entry.UseStyleRef)
                        dictStream.Write(StyleRefBin);
                    dictStream.Write(entry.ContentBin);
                    if (!sameTypeHtml)
                        dictStream.Write(new byte[] { 0 });

                    nWord++;
                    progressCallback?.Invoke(entry.idx);
                }
            }

            var dictZipProc = Process.Start(new ProcessStartInfo("dictzip", $"\"{dictFileName}\"") {
                UseShellExecute = false,
                WorkingDirectory = outputDirPath,
            });
            dictZipProc.WaitForExit();
            if (dictZipProc.ExitCode != 0)
                throw new Exception("dictzip process has closed with the exit code " + dictZipProc.ExitCode);

            File.WriteAllLines(Path.Combine(outputDirPath, fileNameBase + ".ifo"), new[] {
                "StarDict's dict ifo file",
                "version=3.0.0",
                $"wordcount={nWord}",
                $"idxfilesize={idxFileSize}",
                $"bookname=Lạc Việt - {dictName}",
                "author=Meigyoku Thmn",
                "email=tranhuynhminhngoc1994@gmail.com",
                $"date=",
                $"description={description}",
            }.Concat(sameTypeHtml ? new[] { "sametypesequence=h" } : Array.Empty<string>()));

            CssPool.TryGetValue(styleSheetFileName, out var cssContent);
            if (cssContent == null)
            {
                cssContent = File.ReadAllText(Path.Combine("Styles", styleSheetFileName));
                cssContent = MarkupRegex.Replace(cssContent, m => MarkupMap[m.Value].ToString());
                CssPool[styleSheetFileName] = cssContent;
            }
            if (styleSheetCallback != null)
            {
                styleSheetCallback(OutputCssFileName, cssContent);
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(outputDirPath, "res"));
                File.WriteAllText(Path.Combine(outputDirPath, "res", OutputCssFileName), cssContent);
            }
        }
    }
}

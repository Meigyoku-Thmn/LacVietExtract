using HtmlAgilityPack;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

using Seeds = System.Collections.Generic.Dictionary<uint, string>;

namespace Common
{
    public static class Tools
    {
        public static void NormalizeDbFile(string dbPath, DbFileType? dbType)
        {
            if (dbType == null)
                throw new ArgumentException("Chưa cấu hình loại cơ sở dữ liệu.");
            if (dbType == DbFileType.SQLite3)
            {
                using var file = new FileStream(dbPath, FileMode.Open, FileAccess.Write);
                file.Write(Encoding.ASCII.GetBytes("SQLite format 3"));
            }
        }

        public static void PrepareDatabase(string dbPath, string cloneDbPath, DbFileType? dbType)
        {
            CopyIfNewer(dbPath, cloneDbPath);
            NormalizeDbFile(cloneDbPath, dbType);
        }

        public static void CopyIfNewer(string sourcePath, string destPath)
        {
            if (File.GetLastWriteTimeUtc(sourcePath) <= File.GetLastWriteTimeUtc(destPath))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
            File.Copy(sourcePath, destPath, true);
        }

        public static string ReduceGarbage(string content)
        {
            return content.Substring(0, content
                .TakeWhile(chr => !char.IsControl(chr) || chr == '\n' || chr == '\r' || chr == '\t').Count());
        }

        public static string ResolveLacVietMarkups(string content)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(content);
            var codePageElements = doc.DocumentNode.Descendants().ToArray();
            var elementPool = codePageElements.ToHashSet();
            foreach (var e in codePageElements)
            {
                if (!elementPool.Contains(e))
                    continue;
                if (e.NodeType == HtmlNodeType.Text)
                {
                    var textNode = e as HtmlTextNode;
                    textNode.Text = Encoding.GetEncoding(1258)
                        .GetString(Encoding.Latin1.GetBytes(textNode.Text))
                        .Normalize();
                }
                else if (e.NodeType == HtmlNodeType.Element && e.Name.Length > 2 && char.ToLower(e.Name[0]) == 'c')
                {
                    var realEncoding = Encoding.GetEncoding(int.Parse(e.Name[1..]));
                    var unicodeText = realEncoding
                        .GetString(Encoding.Latin1.GetBytes(e.InnerHtml))
                        .Normalize();
                    e.ParentNode.ReplaceChild(doc.CreateTextNode(unicodeText), e);
                    elementPool.Remove(e);
                    foreach (var chl in e.Descendants())
                        elementPool.Remove(chl);
                }
                else if (e.NodeType == HtmlNodeType.Element && e.Name == "b")
                {
                    var refNode = e;
                    foreach (var newChild in e.ChildNodes)
                    {
                        e.ParentNode.InsertAfter(newChild, e);
                        refNode = newChild;
                    }
                    e.Remove();
                    elementPool.Remove(e);
                }
            }
            doc.DocumentNode.AppendChild(doc.CreateTextNode("<eof>"));
            return WebUtility.HtmlDecode(doc.DocumentNode.OuterHtml);
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

        public unsafe static int DecodeBinaryInPlace(byte[] data)
        {
            byte currentByte;
            int nextByte;
            int cursorIdx;
            int targetIndex;
            int immediateByte;

            currentByte = data[0];
            nextByte = data[1];
            cursorIdx = 1;
            targetIndex = 0;
            if (data[1] == 0)
                return targetIndex;

            while (true)
            {
                ++cursorIdx;
                if (nextByte == 1)
                {
                    immediateByte = data[cursorIdx++];

                    switch (immediateByte)
                    {
                        case 1:
                            nextByte = 0;
                            goto LABEL_9;
                        case 2:
                            nextByte = 1;
                            goto LABEL_9;
                        case 3:
                            nextByte = 39;
                            goto LABEL_9;
                    }
                    throw new Exception("Invalid data.");
                }

            LABEL_9:
                data[targetIndex] = (byte)(nextByte + currentByte);
                if (cursorIdx >= data.Length)
                    return data.Length;
                nextByte = data[cursorIdx];
                ++targetIndex;
                if (data[cursorIdx] == 0)
                    return targetIndex;
            }
        }

        public static bool DecryptBinaryInPlace(Span<byte> data, Seeds seeds, bool allowSkip = false)
        {
            var magicCode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4));

            if (!seeds.TryGetValue(magicCode, out var seed) && !allowSkip)
                throw new ArgumentException("Unknown binary type.");
            if (seed == null)
                return false;

            var cipherKey = new CipherKey(seed);
            var idx = 4;
            do
            {
                data[idx++] ^= cipherKey.Advance();
            }
            while (idx < data.Length);

            return true;
        }

        public static uint HashKeyword(byte[] keyword)
        {
            uint hashCode = 0xFFFFFFFF;
            foreach (var e in keyword)
                hashCode = HashKey[(byte)(hashCode ^ e)] ^ (hashCode >> 8);
            return ~hashCode;
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
    }

    class CipherKey
    {
        public byte[] seed;
        public uint key1 = 0x13579BDF;
        public uint key2 = 0x2468ACE0;
        public uint key3 = 0xFDB97531;
        public uint key4 = 0x80000062;
        public uint key5 = 0x40000020;
        public uint key6 = 0x10000002;
        public uint key7 = 0x7FFFFFFF;
        public uint key8 = 0x3FFFFFFF;
        public uint key9 = 0xFFFFFFF;
        public uint key10 = 0x80000000;
        public uint key11 = 0xC0000000;
        public uint key12 = 0xF0000000;

        public CipherKey(string seed)
        {
            this.seed = Encoding.Latin1.GetBytes(seed);
            for (var _seedIdx = 0; _seedIdx < 4; ++_seedIdx)
            {
                key1 <<= 8;
                key1 |= this.seed[_seedIdx];
                key2 <<= 8;
                key2 |= this.seed[_seedIdx + 4];
                key3 <<= 8;
                key3 |= this.seed[_seedIdx + 8];
            }
            if (key1 == 0)
                key1 = 0x13579BDF;
            if (key2 == 0)
                key2 = 0x2468ACE0;
            if (key3 == 0)
                key3 = 0xFDB97531;
        }
        public byte Advance()
        {
            uint v2; byte v3; uint v4; uint v5; uint v6; uint v7; uint v8; uint v9;
            uint v10; uint v11; uint v12; uint v13; uint v14; uint v15; uint v16; uint v17; uint v18;
            uint v19; byte key; byte v21; byte v22; byte v23; byte v24; byte v25; byte v26;

            v2 = key1;
            v3 = (byte)(key2 & 1);
            v21 = 0;
            v25 = v3;
            v26 = (byte)(key3 & 1);
            v4 = 2;

            while (true)
            {
                if ((v2 & 1) != 0)
                {
                    v5 = v2 ^ (key4 >> 1);
                    v6 = key2;
                    v7 = key10 | v5;
                    if ((v6 & 1) != 0)
                    {
                        key2 = key11 | (v6 ^ (key5 >> 1));
                        v3 = 1;
                        v25 = 1;
                    }
                    else
                    {
                        v3 = 0;
                        key2 = key8 & (v6 >> 1);
                        v25 = 0;
                    }
                }
                else
                {
                    v7 = key7 & (v2 >> 1);
                    v8 = key3;
                    if ((v8 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v8 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v8 >> 1);
                    }
                }
                v22 = (byte)((byte)(2 * v21) | (v26 ^ v3));
                if ((v7 & 1) != 0)
                {
                    v9 = v7 ^ (key4 >> 1);
                    v10 = key2;
                    v11 = key10 | v9;
                    if ((v10 & 1) != 0)
                    {
                        v25 = 1;
                        key2 = key11 | (v10 ^ (key5 >> 1));
                    }
                    else
                    {
                        v25 = 0;
                        key2 = key8 & (v10 >> 1);
                    }
                }
                else
                {
                    v11 = key7 & (v7 >> 1);
                    v12 = key3;
                    if ((v12 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v12 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v12 >> 1);
                    }
                }
                v23 = (byte)((byte)(2 * v22) | (v26 ^ v25));
                if ((v11 & 1) != 0)
                {
                    v13 = v11 ^ (key4 >> 1);
                    v14 = key2;
                    v15 = key10 | v13;
                    if ((v14 & 1) != 0)
                    {
                        v25 = 1;
                        key2 = key11 | (v14 ^ (key5 >> 1));
                    }
                    else
                    {
                        v25 = 0;
                        key2 = key8 & (v14 >> 1);
                    }
                }
                else
                {
                    v15 = key7 & (v11 >> 1);
                    v16 = key3;
                    if ((v16 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v16 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v16 >> 1);
                    }
                }
                v24 = (byte)((byte)(2 * v23) | (v26 ^ v25));
                if ((v15 & 1) != 0)
                {
                    v17 = v15 ^ (key4 >> 1);
                    v18 = key2;
                    v2 = key10 | v17;
                    if ((v18 & 1) != 0)
                    {
                        v25 = 1;
                        key2 = key11 | (v18 ^ (key5 >> 1));
                    }
                    else
                    {
                        v25 = 0;
                        key2 = key8 & (v18 >> 1);
                    }
                }
                else
                {
                    v2 = key7 & (v15 >> 1);
                    v19 = key3;
                    if ((v19 & 1) != 0)
                    {
                        v26 = 1;
                        key3 = key12 | (v19 ^ (key6 >> 1));
                    }
                    else
                    {
                        v26 = 0;
                        key3 = key9 & (v19 >> 1);
                    }
                }
                key = (byte)((byte)(2 * v24) | (v26 ^ v25));
                --v4;
                v21 = key;
                if (v4 == 0)
                    break;
                v3 = v25;
            }
            key1 = v2;

            return key;
        }
    };
}

using BerkeleyDB;
using MetaKitWrapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using static System.StringSplitOptions;
using static Common.Tools;

namespace Common
{
    using Seeds = Dictionary<uint, string>;

    public enum DbFileType { SQLite3, Berkeley, MetakitArchive }

    public abstract class DictConnection : IDisposable
    {
        protected readonly bool AllowAdditionalProcessing;
        protected readonly Encoding KeywordEncoding;
        protected readonly Seeds Seeds;

        public static DictConnection Create(
            string dbPath, DbFileType dbType, Encoding keywordEncoding, string tempDirName, Seeds seeds)
        {
            if (dbType == DbFileType.SQLite3)
                return new Sqlite3Connection(dbPath, keywordEncoding, tempDirName, seeds);
            if (dbType == DbFileType.Berkeley)
                return new BerkeleyConnection(dbPath, keywordEncoding, tempDirName, seeds);
            if (dbType == DbFileType.MetakitArchive)
                return new MetakitArchiveConnection(dbPath, keywordEncoding, tempDirName, seeds);

            throw new NotSupportedException($"DbFileType '{dbType}' chưa được hỗ trợ.");
        }

        internal DictConnection(ref string dbPath, Encoding keywordEncoding, string tempDirName, Seeds seeds)
        {
            Seeds = seeds;
            KeywordEncoding = keywordEncoding;
            var cloneDbPath = Path.Combine(tempDirName, Path.GetFileName(dbPath));
            AllowAdditionalProcessing = CopyIfNewer(dbPath, cloneDbPath);
            dbPath = cloneDbPath;
        }

        public abstract IEnumerable<(uint hash, string word)> ReadWords();
        public abstract IEnumerable<(uint hash, string content)> ReadEntries();
        public abstract int CountEntries();
        public abstract string ReadEntryData(string word, uint hash);
        public abstract void Dispose();

        public static string IFF(string cond, string trueVal, string falseVal)
            => $"CASE WHEN {cond} THEN {trueVal} ELSE {falseVal} END";
    }

    public class Sqlite3Connection : DictConnection
    {
        readonly SQLiteConnection _conn;

        public Sqlite3Connection(string dbPath, Encoding keywordEncoding, string tempDirName, Seeds seeds)
            : base(ref dbPath, keywordEncoding, tempDirName, seeds)
        {
            if (AllowAdditionalProcessing)
                using (var file = new FileStream(dbPath, FileMode.Open, FileAccess.Write))
                    file.Write(Encoding.ASCII.GetBytes("SQLite format 3"));

            _conn = new SQLiteConnection($@"Data Source={dbPath};Read Only=True");
            _conn.Open();
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            _conn?.Dispose();
        }

        public override IEnumerable<(uint hash, string word)> ReadWords()
        {
            var query = $@"
                WITH Hash(Id, Ord) AS (
                  {"VALUES" + string.Join(",", Enumerable.Range(0, 100)
                    .Select(ord => $"({HashKeyword(Encoding.ASCII.GetBytes($"m_block_{ord}"))},{ord})"))}
                )
                SELECT
                    mlob.cd                                                         AS data,
                    {IFF("mblklen.cd IS NULL", "LENGTH(mlob.cd)", "mblklen.cd")}    AS encryptedSize
                FROM mlob
                LEFT JOIN mblklen ON mlob.ab = mblklen.ab
                JOIN Hash ON mlob.ab = Hash.id
                ORDER BY Hash.Ord
            ";

            using var cmd = new SQLiteCommand(query, _conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var data = reader["data"] as byte[];
                var encryptedSize = (int)(long)reader["encryptedSize"];
                DecodeBinaryInPlace(data);
                DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), Seeds);

                var _words = KeywordEncoding
                    .GetString(data.AsSpan(4, encryptedSize - 4))
                    .Split(new[] { '\0', '␀' }, RemoveEmptyEntries);

                foreach (var word in _words)
                    yield return (HashKeyword(KeywordEncoding.GetBytes(word)), word);
            }
        }

        public override int CountEntries()
        {
            var query = $@"
                SELECT COUNT(*)
                FROM mabcdef
            ";
            return (int)(long)new SQLiteCommand(query, _conn).ExecuteScalar();
        }

        public override IEnumerable<(uint hash, string content)> ReadEntries()
        {
            var query = $@"
                SELECT
                    mabcdef.ab                                                              AS hash,
                    mabcdef.cd                                                              AS data,                    
                    {IFF("mabcdeflen.cd IS NULL", "LENGTH(mabcdef.cd)", "mabcdeflen.cd")}   AS encryptedSize
                FROM mabcdef
                LEFT JOIN mabcdeflen ON mabcdef.ab = mabcdeflen.ab
                ORDER BY hash
            ";
            using var cmd = new SQLiteCommand(query, _conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hash = (uint)reader["hash"];
                var data = reader["data"] as byte[];
                var encryptedSize = (int)(long)reader["encryptedSize"];
                DecodeBinaryInPlace(data);
                var content = DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), Seeds, allowSkip: true)
                    ? Encoding.Latin1.GetString(data.AsSpan(4, encryptedSize - 4))
                    : Encoding.Latin1.GetString(data.AsSpan(0, encryptedSize));
                yield return (hash, content);
            }
        }

        public override string ReadEntryData(string _, uint hash)
        {
            var query = @$"
                SELECT
                    mabcdef.cd                                                              AS data,
                    {IFF("mabcdeflen.cd IS NULL", "LENGTH(mabcdef.cd)", "mabcdeflen.cd")}   AS encryptedSize
                FROM mabcdef
                LEFT JOIN mabcdeflen ON mabcdef.ab = mabcdeflen.ab
                WHERE mabcdef.ab = {hash}
            ";
            using var cmd = new SQLiteCommand(query, _conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var data = (byte[])reader["data"];
                var encryptedSize = (int)(long)reader["encryptedSize"];
                DecodeBinaryInPlace(data);
                return DecryptBinaryInPlace(data.AsSpan(0, encryptedSize), Seeds, allowSkip: true)
                    ? Encoding.Latin1.GetString(data.AsSpan(4, encryptedSize - 4))
                    : Encoding.Latin1.GetString(data.AsSpan(0, encryptedSize));
            }
            return default;
        }
    }

    public class BerkeleyConnection : DictConnection
    {
        readonly Database _block; // word => hash
        readonly Database _content; // hash => definition

        public BerkeleyConnection(string dbPath, Encoding keywordEncoding, string tempDirName, Seeds seeds)
            : base(ref dbPath, keywordEncoding, tempDirName, seeds)
        {
            // the lib cannot handle unicode path
            var currentDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.GetDirectoryName(dbPath));
            var dbFileName = Path.GetFileName(dbPath);
            try
            {
                _block = Database.Open(dbFileName, "BLOCK", new DatabaseConfig {
                    ReadOnly = true,
                });
                _content = Database.Open(dbFileName, "CONTENT", new DatabaseConfig {
                    ReadOnly = true,
                });
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDir);
            }
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            _block?.Dispose();
            _content?.Dispose();
        }

        public override IEnumerable<(uint hash, string word)> ReadWords()
            => _block.Cursor().Select(e => (HashKeyword(e.Key.Data), KeywordEncoding.GetString(e.Key.Data)));

        public override int CountEntries()
        {
            if (_content is not BTreeDatabase btree)
                throw new NotImplementedException(
                    "The Berkeley database is not BTreeDatabase, counting is not implemented in this case.");
            var count = (int)btree.FastStats(null, Isolation.DEGREE_ONE).nKeys;
            if (count == 0)
                count = (int)btree.Stats(null, Isolation.DEGREE_ONE).nKeys;
            return count;
        }

        public override IEnumerable<(uint hash, string content)> ReadEntries()
        {
            return _content.Cursor().Select(e => {
                var hash = uint.Parse(Encoding.ASCII.GetString(e.Key.Data));
                var data = e.Value.Data;
                var content = DecryptBinaryInPlace(data.AsSpan(0, data.Length), Seeds, allowSkip: true)
                    ? Encoding.Latin1.GetString(data.AsSpan(4, data.Length - 4))
                    : Encoding.Latin1.GetString(data.AsSpan(0, data.Length));
                return (hash, content);
            });
        }

        public override string ReadEntryData(string _, uint hash)
        {
            try
            {
                var data = _content.Get(new DatabaseEntry(Encoding.ASCII.GetBytes($"{hash}"))).Value.Data;
                return DecryptBinaryInPlace(data.AsSpan(0, data.Length), Seeds, allowSkip: true)
                    ? Encoding.Latin1.GetString(data.AsSpan(4, data.Length - 4))
                    : Encoding.Latin1.GetString(data.AsSpan(0, data.Length));
            }
            catch (NotFoundException)
            {
                return default;
            }
        }
    }

    public class MetakitArchiveConnection : DictConnection
    {
        readonly Dictionary<string, int> _blocks = new(112311);
        readonly Dictionary<int, MetaKit> _conns = new(100);

        public MetakitArchiveConnection(string dbPath, Encoding keywordEncoding, string tempDirName, Seeds seeds)
            : base(ref dbPath, keywordEncoding, tempDirName, seeds)
        {
            tempDirName = Path.Combine(tempDirName, Path.GetFileNameWithoutExtension(dbPath));
            Directory.CreateDirectory(tempDirName);
            if (AllowAdditionalProcessing)
            {
                using var reader = new BinaryReader(File.OpenRead(dbPath));
                reader.BaseStream.Seek(0x10 + 8, SeekOrigin.Begin);
                var nextPos = reader.ReadInt32();

                while (nextPos > 0)
                {
                    reader.BaseStream.Seek(nextPos, SeekOrigin.Begin);
                    var currentPos = nextPos;
                    var dataPos = reader.ReadInt32();
                    var dataSize = reader.ReadInt32();
                    nextPos = reader.ReadInt32();
                    var blockName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadByte()));

                    reader.BaseStream.Seek(dataPos, SeekOrigin.Begin);
                    
                    ResolveAndExtract(tempDirName, blockName, reader.ReadBytes(dataSize));
                }
            }

            // the lib cannot handle unicode path
            var currentDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(tempDirName);
            try
            {
                for (var i = 0; i <= 99; i++)
                {
                    foreach (var word in File.ReadAllLines($"BLKWRD{i}.txt"))
                        _blocks.Add(word, i);
                    var conn = new MetaKit();
                    var dbFileName = $"Content{i}.mk";
                    if (!conn.OpenDB(dbFileName))
                        throw new Exception($"Cannot open Metakit database '{dbFileName}'");
                    _conns.Add(i, conn);
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDir);
            }
        }

        void ResolveAndExtract(string outputDirPath, string name, byte[] data)
        {
            var outputPath = Path.Combine(outputDirPath, name);

            if (name.StartsWith("Content") || name.StartsWith("CT"))
            {
                using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                using var outputStream = File.Create(outputPath + ".mk");
                gzip.CopyTo(outputStream);
            }
            else if (name.StartsWith("BLKWRD"))
            {
                var words = KeywordEncoding.GetString(data)
                    .Split(new[] { '\0', '␀' }, RemoveEmptyEntries);
                File.WriteAllLines(outputPath + ".txt", words);
            }
            else if (name.StartsWith("N"))
            {
                var reader = new BinaryReader(new MemoryStream(data));
                var lengths = new List<int>();
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                    lengths.Add(reader.ReadInt32());
                File.WriteAllLines(outputPath + ".txt", lengths.Select((e, i) => "BLOCK " + i + ": " + e.ToString()));
            }
            else
            {
                File.WriteAllBytes(outputPath + ".dump", data);
            }
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            foreach (var conn in _conns.Values)
                conn.Dispose();
        }

        public override string ReadEntryData(string word, uint hash)
        {
            if (!_blocks.TryGetValue(word, out var blockIdx))
                return default;

            var conn = _conns[blockIdx];
            var data = conn.FindBinaryByKey(conn.OpenView("content"), hash, 0, 1);
            if (data == null)
                return default;

            return DecryptBinaryInPlace(data.AsSpan(0, data.Length), Seeds, allowSkip: true)
                ? Encoding.Latin1.GetString(data.AsSpan(4, data.Length - 4))
                : Encoding.Latin1.GetString(data.AsSpan(0, data.Length));
        }

        public override IEnumerable<(uint hash, string word)> ReadWords()
            => _blocks.Select(e => (HashKeyword(KeywordEncoding.GetBytes(e.Key)), e.Key));

        public override int CountEntries() => _blocks.Values
            .Distinct()
            .Select(idx => {
                var conn = _conns[idx];
                var viewIdx = conn.OpenView("content");
                return conn.GetRowCount(viewIdx);
            })
            .Sum();

        public override IEnumerable<(uint hash, string content)> ReadEntries()
        {
            foreach (var conn in _blocks.Values.Distinct().Select(idx => _conns[idx]))
            {
                var viewIdx = conn.OpenView("content");
                var count = conn.GetRowCount(viewIdx);
                for (var rowIdx = 0; rowIdx < count; rowIdx++)
                {
                    var pair = conn.GetKeyBinaryValuePair(viewIdx, rowIdx, 0, 1);
                    if (pair.Value == null)
                        continue;
                    var hash = (uint)pair.Key;
                    var data = pair.Value;

                    var content = DecryptBinaryInPlace(data.AsSpan(0, data.Length), Seeds, allowSkip: true)
                        ? Encoding.Latin1.GetString(data.AsSpan(4, data.Length - 4))
                        : Encoding.Latin1.GetString(data.AsSpan(0, data.Length));
                    yield return (hash, content);
                }
            }
        }
    }
}

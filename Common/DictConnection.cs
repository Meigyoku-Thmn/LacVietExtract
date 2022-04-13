using BerkeleyDB;
using Lucene.Net.Index;
using MetaKitWrapper;
using MoreLinq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using static Common.Tools;
using static System.StringSplitOptions;

namespace Common
{
    using Seeds = Dictionary<uint, string>;

    public enum DbFileType { SQLite3, Berkeley, MetakitArchive, Lucene, ImageArchive }

    public abstract class DictConnection : IDisposable
    {
        protected readonly bool AllowAdditionalProcessing;
        protected readonly Encoding KeywordEncoding;
        protected readonly Seeds Seeds;

        public static DictConnection Create(string dbPath,
            DbFileType dbType, Encoding keywordEncoding, string tempDirName,
            Seeds seeds, Dictionary<string, string> fileNameMap = null)
        {
            if (dbType == DbFileType.SQLite3)
                return new Sqlite3Connection(dbPath, keywordEncoding, tempDirName, seeds);
            if (dbType == DbFileType.Berkeley)
                return new BerkeleyConnection(dbPath, keywordEncoding, tempDirName, seeds);
            if (dbType == DbFileType.MetakitArchive)
                return new MetakitArchiveConnection(dbPath, keywordEncoding, tempDirName, seeds);
            if (dbType == DbFileType.Lucene)
                return new LuceneConnection(dbPath, keywordEncoding, tempDirName, seeds, fileNameMap);

            throw new NotSupportedException($"DbFileType '{dbType}' chưa được hỗ trợ.");
        }

        internal DictConnection(ref string dbPath, Encoding keywordEncoding, string tempDirName,
            Seeds seeds, Dictionary<string, string> fileNameMap = null)
        {
            Seeds = seeds;
            KeywordEncoding = keywordEncoding;
            var cloneDbPath = Path.Combine(tempDirName, Path.GetFileName(dbPath));
            AllowAdditionalProcessing = CopyIfNewer(dbPath, cloneDbPath);
            if (AllowAdditionalProcessing && fileNameMap != null)
                RenameByFileNameMap(cloneDbPath, fileNameMap);
            dbPath = cloneDbPath;
        }

        static Exception NotImpl() => new NotImplementedException();

        public virtual IEnumerable<(uint hash, string word)> ReadWords() => throw NotImpl();
        public virtual IEnumerable<(uint hash, string content)> ReadEntries() => throw NotImpl();
        public virtual int CountEntries() => throw NotImpl();
        public virtual string ReadEntryData(string word, uint hash) => throw NotImpl();
        public virtual int CountRelateds() => throw NotImpl();
        public virtual IEnumerable<(uint hash, string content)> ReadRelateds() => throw NotImpl();
        public virtual int CountImageCatalogGroups() => throw NotImpl();
        public virtual IEnumerable<IGrouping<int, CatalogEntry>> ReadImageCatalogGroups() => throw NotImpl();
        public virtual int CountGrammarItems() => throw NotImpl();
        public virtual IEnumerable<IGrammarItem> ReadGrammarItems() => throw NotImpl();
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
        public override int CountRelateds()
        {
            var query = $@"
                SELECT COUNT(DISTINCT ab)
                FROM k_COMPOUNDS
            ";
            return (int)(long)new SQLiteCommand(query, _conn).ExecuteScalar();
        }

        public override IEnumerable<(uint hash, string content)> ReadRelateds()
        {
            var query = $@"
                SELECT
                    k_COMPOUNDS.ab                                                              AS hash,
                    k_COMPOUNDS.cd                                                              AS data,
                    {IFF("l_COMPOUNDS.cd IS NULL", "LENGTH(k_COMPOUNDS.cd)", "l_COMPOUNDS.cd")} AS decodedSize
                FROM k_COMPOUNDS
                LEFT JOIN l_COMPOUNDS ON k_COMPOUNDS.ab = l_COMPOUNDS.ab
                GROUP BY k_COMPOUNDS.ab
                ORDER BY hash
            ";
            using var cmd = new SQLiteCommand(query, _conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hash = (uint)reader["hash"];
                var data = reader["data"] as byte[];
                var decodedSize = (int)(long)reader["decodedSize"];
                DecodeBinaryInPlace(data);
                var content = Encoding.Latin1.GetString(data.AsSpan(0, decodedSize));
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

        public override int CountImageCatalogGroups()
        {
            var query = $@"
                SELECT COUNT(*)
                FROM 'parentcatalo' p
            ";
            return (int)(long)new SQLiteCommand(query, _conn).ExecuteScalar();
        }

        public override IEnumerable<IGrouping<int, CatalogEntry>> ReadImageCatalogGroups()
        {
            var rowNumberCond = "ROW_NUMBER() OVER (PARTITION BY p.id) > 1";
            var query = $@"
                SELECT
                   p.id                                     AS 'Category Id',
                   {IFF(rowNumberCond, "NULL", "p.name")}   AS 'Category Name',
                   {IFF(rowNumberCond, "NULL", "p.image")}  AS 'Category Image',
                   c.id                                     AS 'Topic Id',
                   c.name                                   AS 'Topic Name',
                   c.name_vn                                AS 'Topic Name Translated',
                   c.image                                  AS 'Topic Image'
                FROM 'parentcatalo' p
                JOIN 'catalo' c WHERE p.id = c.id_parent
                ORDER BY p.id
            ";
            using var cmd = new SQLiteCommand(query, _conn);
            using var reader = cmd.ExecuteReader();
            IEnumerable<CatalogEntry> ReadAsCollection()
            {
                while (reader.Read())
                {
                    var categoryImage = reader.Get<byte[]>("Category Image");
                    DecryptExtraContentInPlace(categoryImage);
                    var topicImage = reader.Get<byte[]>("Topic Image");
                    DecryptExtraContentInPlace(topicImage);
                    yield return new CatalogEntry {
                        CategoryId = (int)reader.Get<long>("Category Id"),
                        CategoryName = reader.Get<string>("Category Name"),
                        CategoryImage = categoryImage,
                        TopicId = (int)reader.Get<long>("Topic Id"),
                        TopicName = reader.Get<string>("Topic Name"),
                        TopicNameTranslated = reader.Get<string>("Topic Name Translated"),
                        TopicImage = topicImage,
                    };
                }
            }
            foreach (var group in ReadAsCollection().GroupAdjacent(e => e.CategoryId))
                yield return group;
        }

        public override int CountGrammarItems()
        {
            var query = @$"
                SELECT
                    (SELECT COUNT(*) FROM 'Elementary') +
                    (SELECT COUNT(*) FROM 'Intermediate') +
                    (SELECT COUNT(*) FROM 'Advanced');
            ";
            return (int)(long)new SQLiteCommand(query, _conn).ExecuteScalar();
        }

        public override IEnumerable<IGrammarItem> ReadGrammarItems()
        {
            var rowNumberCond1 = "ROW_NUMBER() OVER (PARTITION BY LevelId) > 1";
            var rowNumberCond2 = "ROW_NUMBER() OVER (PARTITION BY LevelId, topic) > 1";
            var denseRank = "DENSE_RANK() OVER (ORDER BY LevelId, topic)";
            var query = $@"
                SELECT
                    LevelId                                     AS 'LevelId',
                    {IFF(rowNumberCond1, "NULL", "Level")}      AS 'Level',
                    {IFF(rowNumberCond1, "NULL", "LevelVi")}    AS 'LevelVi',
                    {denseRank}                                 AS 'TopicId',
                    {IFF(rowNumberCond2, "NULL", "topic")}      AS 'Topic',
                    lesson                                      AS 'Lesson',
                    file                                        AS 'File',
                    title_exercises                             AS 'ExerciseTitle',
                    exercises                                   AS 'Exercise'
                FROM (
                    SELECT
                        'Elementary' AS Level,
                        'Sơ cấp' AS LevelVi,
                        1 AS LevelId,
                        *
                    FROM 'Elementary'
                    UNION
                    SELECT
                        'Intermediate' AS Level,
                        'Trung cấp' AS LevelVi,
                        2 AS LevelId,
                        *
                    FROM 'Intermediate'
                    UNION
                    SELECT
                        'Advanced' AS Level,
                        'Cao cấp' AS LevelVi,
                        3 AS LevelId,
                        *
                    FROM 'Advanced'
                )
                ORDER BY LevelId, lesson_id
            ";
            using var cmd = new SQLiteCommand(query, _conn);
            using var reader = cmd.ExecuteReader();

            var lastLevelId = default(int?);
            var lastLevelTitle = default(string);
            var lastLevelTitleVi = default(string);

            var lastTopicId = default(int?);
            var lastTopicTitle = default(string);

            while (reader.Read())
            {
                var levelId = (int)reader.Get<long>("LevelId");
                var level = reader.Get<string>("Level");
                var levelVi = reader.Get<string>("LevelVi");
                var topicId = (int)reader.Get<long>("TopicId");
                var topic = reader.Get<string>("Topic")?.Normalize().Trim();
                var lesson = reader.Get<string>("Lesson")?.Normalize().Trim();
                var fileArr = reader.Get<byte[]>("File");
                DecryptExtraContentInPlace(fileArr);
                var file = Encoding.UTF8.GetString(fileArr).Normalize();
                var exerciseTitleArr = reader.Get<byte[]>("ExerciseTitle");
                DecryptExtraContentInPlace(exerciseTitleArr);
                var exerciseTitle = Encoding.UTF8.GetString(exerciseTitleArr).Normalize();
                var exerciseArr = reader.Get<byte[]>("Exercise");
                DecryptExtraContentInPlace(exerciseArr);
                var exercise = Encoding.UTF8.GetString(exerciseArr).Normalize();

                if (lastTopicId != topicId)
                {
                    if (lastTopicId != null)
                    {
                        yield return new GrammarTopic {
                            Id = lastTopicId.Value,
                            Title = lastTopicTitle,
                        };
                    }
                    lastTopicId = topicId;
                    lastTopicTitle = topic;
                }

                if (lastLevelId != levelId)
                {
                    if (lastLevelId != null)
                    {
                        yield return new GrammarLevel {
                            Id = lastLevelId.Value,
                            Title = lastLevelTitle,
                            TitleVi = lastLevelTitleVi,
                        };
                    }
                    lastLevelId = levelId;
                    lastLevelTitle = level;
                    lastLevelTitleVi = levelVi;
                }

                yield return new GrammarLesson {
                    Title = lesson,
                    Content = file,
                    ExerciseTitle = exerciseTitle,
                    ExerciseContent = exercise,
                    Level = lastLevelTitle,
                    LevelVi = lastLevelTitleVi,
                    Topic = lastTopicTitle,
                };
            }

            yield return new GrammarTopic {
                Id = lastTopicId.Value,
                Title = lastTopicTitle,
            };
            yield return new GrammarLevel {
                Id = lastLevelId.Value,
                Title = lastLevelTitle,
                TitleVi = lastLevelTitleVi,
            };
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

    public class LuceneConnection : DictConnection
    {
        readonly IndexReader _indexReader;

        public LuceneConnection(string dbPath, Encoding keywordEncoding, string tempDirName,
            Seeds seeds, Dictionary<string, string> fileNameMap)
            : base(ref dbPath, keywordEncoding, tempDirName, seeds, fileNameMap)
        {
            _indexReader = IndexReader.Open(Lucene.Net.Store.FSDirectory.Open(dbPath), true);
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
            _indexReader?.Dispose();
        }

        public override int CountEntries() => _indexReader.NumDocs();

        public override IEnumerable<(uint hash, string content)> ReadEntries()
        {
            for (int i = 0; i < _indexReader.MaxDoc; i++)
            {
                if (_indexReader.IsDeleted(i))
                    continue;
                var doc = _indexReader.Document(i);
                foreach (var hashStr in _indexReader.GetTermFreqVector(i, "Content").GetTerms())
                    yield return (uint.Parse(hashStr), doc.Get("Word"));
            }

        }
    }
}

using BerkeleyDB;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public enum DbFileType { SQLite3, Berkeley, MFCSerialized }

    public class FileConnection : IDisposable
    {
        public readonly IDisposable conn;
        public readonly IDisposable conn2;
        public readonly DbFileType type;

        public FileConnection(string dbPath, DbFileType dbType)
        {
            type = dbType;
            if (dbType == DbFileType.SQLite3)
            {
                var _conn = new SQLiteConnection($@"Data Source={dbPath};Read Only=True");
                _conn.Open();
                conn = _conn;
            }
            else if (dbType == DbFileType.Berkeley)
            {
                // It throws an exception if using an unicode path
                var currentDir = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(Path.GetDirectoryName(dbPath));
                var dbFileName = Path.GetFileName(dbPath);
                try
                {
                    conn = Database.Open(dbFileName, "BLOCK", new DatabaseConfig {
                        ReadOnly = true,
                    });
                    conn2 = Database.Open(dbFileName, "CONTENT", new DatabaseConfig {
                        ReadOnly = true,
                    });
                }
                finally
                {
                    Directory.SetCurrentDirectory(currentDir);
                }
            }
            else
                throw new NotSupportedException($"DbFileType '{dbType}' chưa được hỗ trợ.");
        }

        public (byte[] data, int encryptedSize, bool decoded) ReadEntryData(uint hash)
        {
            if (type == DbFileType.SQLite3)
            {
                var query = @$"
                    SELECT
                        mabcdef.cd      AS cd,
                        mabcdeflen.cd   AS len
                    FROM mabcdef
                    JOIN mabcdeflen ON mabcdef.ab = mabcdeflen.ab
                    WHERE mabcdef.ab = {hash}
                ";
                using var cmd = new SQLiteCommand(query, (SQLiteConnection)conn);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var data = (byte[])reader["cd"];
                    var encryptedSize = (uint)reader["len"];
                    return (data, (int)encryptedSize, false);
                }
            }
            else if (type == DbFileType.Berkeley)
            {
                try
                {
                    var data = ((Database)conn2).Get(new DatabaseEntry(Encoding.ASCII.GetBytes($"{hash}"))).Value.Data;
                    return (data, data.Length, true);
                }
                catch (NotFoundException)
                {
                    return (null, 0, false);
                }
            }

            return (null, 0, false);
        }

        public (byte[] data, int encryptedSize) ReadIndexData(uint hash)
        {
            if (type == DbFileType.SQLite3)
            {
                var query = @$"
                    SELECT
                        mlob.cd     AS cd,
                        mblklen.cd  AS len
                    FROM mlob
                    JOIN mblklen ON mlob.ab = mblklen.ab
                    WHERE mlob.ab = {hash}
                ";
                using var cmd = new SQLiteCommand(query, (SQLiteConnection)conn);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var data = (byte[])reader["cd"];
                    var encryptedSize = (uint)reader["len"];
                    return (data, (int)encryptedSize);
                }
            }
            return (null, 0);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            conn.Dispose();
            conn2?.Dispose();
        }
    }
}

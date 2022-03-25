using Common;
using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var config = Config.Get();

            var app = config.Apps.Where(e => e.Name == "Lạc Việt mtd EVA").First();
            var dict = app.Dicts.Where(e => e.Name == "Từ điển Anh-Việt").First();

            var seeds = config.SeedsByName[app.Name];
            var encoding = Encoding.GetEncoding(dict.KeywordEncoding);


            var dbPath = @"C:\Program Files (x86)\LacViet\mtdEVA\LvTHESAURUS.dit";
            var con = DictConnection.Create(dbPath, DbFileType.MetakitArchive, encoding, app.Name, seeds);

            var aaaa = con.ReadEntryData("beautiful", Tools.HashKeyword(Encoding.ASCII.GetBytes("beautiful")));


            //var conn = con.GetType().GetField("_conn", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(con) as SQLiteConnection;

            //var query = @$"
            //    SELECT *
            //    FROM 'Advanced'
            //    WHERE lesson_id = 1
            //";
            //using var cmd = new SQLiteCommand(query, conn);
            //using var reader = cmd.ExecuteReader();
            //reader.Read();

            //var arr = (byte[])reader["exercises"];
            //Tools.DecryptHtmlInPlace(arr);

            //File.WriteAllBytes(@"C:\Users\NgocHuynhMinhTran\Desktop\test.bin", arr);

            //var abc = Encoding.Latin1.GetString(arr);
        }
    }
}

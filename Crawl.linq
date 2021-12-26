<Query Kind="Program">
  <Namespace>System.IO.Compression</Namespace>
  <RuntimeVersion>5.0</RuntimeVersion>
</Query>

string inputPath = @"C:\Program Files (x86)\LacViet\mtdFVP\DATA\LVFV2000.DIT";
string outputDirPath = @"C:\Users\NgocHuynhMinhTran\Desktop\FVP";

void ResolveAndExtract(string name, byte[] data) {
	var outputPath = Path.Combine(outputDirPath, name);
	
	if (name.StartsWith("Content") || name.StartsWith("CT") || name == "INFOR") {
		using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
		using var outputStream = File.Create(outputPath + ".mk");
		gzip.CopyTo(outputStream);
	}
	else if (name.StartsWith("BLKWRD")) {
		var words = Encoding.Latin1.GetString(data)
			.Split("\0", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		File.WriteAllLines(outputPath + ".txt", words);
	}
	else if (name.StartsWith("N")) {
		var reader = new BinaryReader(new MemoryStream(data));
		var lengths = new List<int>();
		while (reader.BaseStream.Position < reader.BaseStream.Length)
			lengths.Add(reader.ReadInt32());
		File.WriteAllLines(outputPath + ".txt", lengths.Select((e, i) => "BLOCK " + i + ": " + e.ToString()));
	}
	else {
		File.WriteAllBytes(outputPath + ".dump", data);
	}
	
	if (name == "INFOR") {
		var encoding = Encoding.GetEncoding(1258);
		var texts = new List<string>();
		using (var reader = new BinaryReader(File.OpenRead(outputPath + ".mk"))) {
			while (reader.BaseStream.Position < reader.BaseStream.Length)
			    texts.Add(encoding.GetString(reader.ReadBytes(reader.ReadByte())));
		}
		File.WriteAllLines(outputPath + ".txt", texts
			.Where(e => e.Length > 0 && e.All(c => c >= ' ')));
		File.Delete(outputPath + ".mk");
	}
}

void Main()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	IEnumerable<object> DumpMetadata() {	
		
		var reader = new BinaryReader(File.OpenRead(inputPath));
		
		reader.BaseStream.Seek(0x10 + 8, SeekOrigin.Begin);		
		var nextPos = reader.ReadInt32();
		
		Console.WriteLine("Dump metadata:");
		
		while (nextPos > 0)  {
		    reader.BaseStream.Seek(nextPos, SeekOrigin.Begin);			
		    var currentPos = nextPos;
		    var dataPos = reader.ReadInt32();
		    var dataSize = reader.ReadInt32();
		    nextPos = reader.ReadInt32();
		    var blockName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadByte()));
			
			reader.BaseStream.Seek(dataPos, SeekOrigin.Begin);			
			ResolveAndExtract(blockName, reader.ReadBytes(dataSize));
			
			yield return new {
				Name = blockName,
				Position = currentPos.ToString("X2"),
				Size = dataSize
			};
		}
	}
	
	DumpMetadata().Dump();
}
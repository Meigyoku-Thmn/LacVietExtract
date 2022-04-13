using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public interface IEntry
    {
        public bool UseStyleRef { get; set; }
        public char Type { get; set; }
        public string Word { get; set; }
        public string Content { get; set; }
    }

    public class DiEntry : IEntry
    {
        public bool UseStyleRef { get; set; } = true;
        public char Type { get; set; } = 'h';
        public uint Hash { get; set; }
        public string Word { get; set; }
        public string Content { get; set; }
        public string RawContent { get; set; }
        public string[] ErrorMessages { get; set; }
    }

    public class CatalogEntry
    {
        public int CategoryId;
        public string CategoryName;
        public byte[] CategoryImage;
        public int TopicId;
        public string TopicName;
        public string TopicNameTranslated;
        public byte[] TopicImage;
    }

    public class ImageEntry
    {
        public int Id;
        public int TopicId;
        public string EnName;
        public string ViName;
        public int EntryOffset;
        public int EntrySize;
    }

    public class GenericEntry : IEntry
    {
        public bool UseStyleRef { get; set; } = true;
        public char Type { get; set; } = 'h';
        public string Word { get; set; }
        public string Content { get; set; }
    }

    public interface IGrammarItem
    {
        public string Title { get; set; }
        public IEnumerable<string> SubKeys { get; set; }
    }

    public class GrammarLevel : IGrammarItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string TitleVi { get; set; }
        public IEnumerable<string> SubKeys { get; set; }
    }

    public class GrammarTopic : IGrammarItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public IEnumerable<string> SubKeys { get; set; }
    }

    public class GrammarLesson : IGrammarItem
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public string ExerciseTitle { get; set; }
        public string ExerciseContent { get; set; }

        public string Level { get; set; }
        public string LevelVi { get; set; }
        public string Topic { get; set; }
        public IEnumerable<string> SubKeys { get; set; }
    }
}

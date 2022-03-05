using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor
{
    using Words = Dictionary<string, uint>;
    using Entries = Dictionary<uint, Entry>;

    public class Patching
    {
        public static bool ApplySingle(Config.App.Dict dict, string word, ref string content)
        {
            var orphanedWords = dict.Patches?.OrphanedWords ?? Array.Empty<string>();
            var corruptedEntries = dict.Patches?.CorruptedEntries ?? new Dictionary<string, object>();
            var substitutions = dict.Patches?.Substitutions ?? Array.Empty<Config.App.Dict._Patches.Substitution>();
            if (orphanedWords.Length == 0 && corruptedEntries.Count == 0 && substitutions.Length == 0) return false;

            var patched = false;
            if (corruptedEntries.TryGetValue(word, out var solution))
            {
                Log.Write($"Fix content for word '{word}'");
                if (solution is int length)
                    content = content.Substring(0, length);
                else if (solution is string altContent)
                    content = altContent;
                patched |= true;
            }

            foreach (var substitution in substitutions)
            {
                if (substitution.Words.Contains(word))
                {
                    if (!patched) Log.Write($"Fix content for word '{word}'");
                    for (var i = 0; i < substitution.Targets.Length; i++)
                    {
                        var target = substitution.Targets[i];
                        var replacement = substitution.Replacements[i];
                        content = content.Replace(target, replacement);
                        patched |= true;
                    }
                }
            }

            return patched;
        }

        public static void Apply(Config.App.Dict dict, Words words, Entries entries)
        {
            var corruptedWords = dict.Patches?.CorruptedWords ?? new Words();
            var orphanedWords = dict.Patches?.OrphanedWords ?? Array.Empty<string>();
            var corruptedEntries = dict.Patches?.CorruptedEntries ?? new Dictionary<string, object>();
            var substitutions = dict.Patches?.Substitutions ?? Array.Empty<Config.App.Dict._Patches.Substitution>();
            if (corruptedWords.Count == 0 && orphanedWords.Length == 0 && corruptedEntries.Count == 0 && substitutions.Length == 0)
            {
                Log.Write("None");
                return;
            }

            foreach (var (word, solution) in corruptedEntries)
            {
                var entry = entries[words[word]];
                Log.Write($"Fix content for word '{word}'");
                if (solution is int length)
                {
                    entry.ErrorMessages = null;
                    entry.Content = entry.Content.Substring(0, length);
                }
                else if (solution is string altContent)
                {
                    entry.ErrorMessages = null;
                    entry.Content = altContent;
                }
            }

            foreach (var substitution in substitutions)
            {
                foreach (var word in substitution.Words)
                {
                    var entry = entries[words[word]];
                    Log.Write($"Fix content for word '{word}'");
                    for (var i = 0; i < substitution.Targets.Length; i++)
                    {
                        var target = substitution.Targets[i];
                        var replacement = substitution.Replacements[i];
                        entry.Content = entry.Content.Replace(target, replacement);
                    }
                    entry.ErrorMessages = null;
                }
            }

            foreach (var (word, hash) in corruptedWords)
            {
                Log.Write(@$"Guess '{word}' for {hash}");
                words[word] = hash;
                words.Remove(word + '�');
            }

            foreach (var word in orphanedWords)
            {
                Log.Write(@$"Remove orphaned word '{word}'");
                words.Remove(word);
            }


        }
    }
}

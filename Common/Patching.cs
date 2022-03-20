using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    using Words = Dictionary<string, uint>;
    using Entries = Dictionary<uint, Entry>;

    public class Patching
    {
        public const string AsIsTag = "<d-as-is>";
        public static bool PreApply(Config.App.Dict dict, string word, ref string content)
        {
            var corruptedEntries = dict.Patches?.CorruptedEntries ?? new Dictionary<string, object>();
            if (corruptedEntries.Count == 0) return false;

            var patched = false;
            if (corruptedEntries.TryGetValue(word, out var solution))
            {
                Log.Write($"Fix content for word '{word}'");
                if (solution is int length)
                    content = content.Substring(0, length);
                else if (solution is string altContent)
                    content = AsIsTag + altContent;
                patched |= true;
            }

            return patched;
        }

        public static void PreApply(Config.App.Dict dict, Words words, Entries entries)
        {
            var corruptedEntries = dict.Patches?.CorruptedEntries ?? new Dictionary<string, object>();
            if (corruptedEntries.Count == 0)
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
                    entry.Content = AsIsTag + altContent;
                }
            }
        }

        public static bool PostApply(Config.App.Dict dict, string word, ref string content)
        {
            var orphanedWords = dict.Patches?.OrphanedWords ?? Array.Empty<string>();
            var substitutions = dict.Patches?.Substitutions ?? Array.Empty<Config.App.Dict._Patches.Substitution>();
            if (orphanedWords.Length == 0 && substitutions.Length == 0) return false;

            var patched = false;
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

        public static void PostApply(Config.App.Dict dict, Words words, Entries entries)
        {
            var corruptedWords = dict.Patches?.CorruptedWords ?? new Words();
            var orphanedWords = dict.Patches?.OrphanedWords ?? Array.Empty<string>();
            var substitutions = dict.Patches?.Substitutions ?? Array.Empty<Config.App.Dict._Patches.Substitution>();
            if (corruptedWords.Count == 0 && orphanedWords.Length == 0 && substitutions.Length == 0)
            {
                Log.Write("None");
                return;
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

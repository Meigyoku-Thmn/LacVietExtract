using HtmlAgilityPack;
using RtfPipe;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Common
{
    public static class Markup
    {
        static readonly Config config = Config.Get();

        static readonly HtmlDocument doc = new();

        static readonly string[] EastAsianLangs = new[] { "Chinese", "Japanese", "Korean" };
        static readonly Regex CodeOpenTagRegex = new(@"<c(\d+)>", RegexOptions.Compiled);
        static readonly Regex CodeCloseOrOpenTagRegex = new(@"</c(\d+)>|<c(\d+)>", RegexOptions.Compiled);
        static readonly Regex SuperScriptTagRegex = new(@"<script>(\d)", RegexOptions.Compiled);
        static readonly Regex FauxTagRegex = new(@"<super>|<subs>\w+<\/subs>|<subs>", RegexOptions.Compiled);
        static readonly Regex FauxTagAndGlitchRegex2 =
            new(@"<china>|<super>\w{0,1}|<subs>\d*|\(方>|\(古>|\(口>|\(书>", RegexOptions.Compiled);
        const string META = "**";
        const string DEF = "``";
        const string EG = "##";
        const string EG_TSL = "~~";
        const string REF = "$$";
        const string PRONO_OPEN = "[[";
        const string PRONO_CLOSE = "]]";
        const string IDIOM = "@@";
        const string IDIOM_TSL = "&&";
        const string IDIOM_EG = "\x19\x19";
        const string IDIOM_EG_TSL = "\x1A\x1A";
        const string DEF2 = "%%";
        const string ALT = "^^";
        const string MEDIA_OPEN = "{{";
        const string MEDIA_CLOSE = "}}";
        const string NEWLINE = "\n";
        const string NEWLINE2 = "\r\n";
        const string TAB = "\t";
        const string RTF = @"\x01[\x03-\x05]\d+(?={\\rtf)";
        const string RTFHeader = @"\x01[\x03-\x05](\d+)";
        const string FF = "\x0C";
        static readonly Regex RtfHeaderMarkupRegex = new(RTFHeader, RegexOptions.Compiled);
        static readonly Regex DictMarkupRegex = new(string.Join('|', new[] { META, DEF, EG, EG_TSL, REF, PRONO_OPEN, PRONO_CLOSE, IDIOM, IDIOM_TSL, IDIOM_EG, IDIOM_EG_TSL, DEF2, ALT, MEDIA_OPEN, MEDIA_CLOSE, NEWLINE, NEWLINE2, TAB, FF }.Select(e => Regex.Escape(e))) + $"|{RTF}", RegexOptions.Compiled);
        static readonly Config.MarkupConfig Markups = config.ConfigMarkup;
        static readonly string[] TagsToUnwrap = new[] { "usage", "http:", "mean_jp" };
        static readonly string[] TagsToRemove = new[] { "left", "input", "c" };
        static readonly (string, string)[] TagNameMap = new[] { ("italic", "i"), ("subs", "sub") };
        static readonly string[] InlineTags = new[] {
            "a", Markups.Tab, Markups.Media, Markups.MetaTitle,
        };
        static readonly Regex RefTagRegex = new(@"(<a[\s\w\d=""]*?)>(.*?)<\/a>", RegexOptions.Compiled);
        static (string content, string[] errorMessages)
            ResolveLactVietDictMarkups(string content, bool useMetaTitle = false, bool fixBulletPoint = false)
        {
            var errorMessages = Array.Empty<string>();
            var contentBuilder = new StringBuilder(content.Length);
            var currentPos = 0;
            var markupStack = new Stack<(string tag, bool textTrimmed)>();
            var previousIsDef = false;
            void EnterTag(string tag, bool pushToStack = true)
            {
                if (!fixBulletPoint || markupStack.Count > 0)
                {
                    contentBuilder.Append($"<{tag}>");
                }
                else if (tag != Markups.Definition)
                {
                    contentBuilder.Append($"<{tag}>");
                    previousIsDef = false;
                }
                else if (previousIsDef == false)
                {
                    contentBuilder.Append($"<{tag}>");
                    previousIsDef = true;
                }
                else
                {
                    contentBuilder.Append($"<{tag} class=\"{Markups.NoBulletClass}\">");
                    previousIsDef = false;
                }

                if (pushToStack)
                    markupStack.Push((tag, false));
            }
            void CloseTags(int count = -1)
            {
                if (count < 0)
                    count = markupStack.Count;
                for (var i = 0; i < count; i++)
                    contentBuilder.Append($"</{markupStack.Pop().tag}>");
            }
            while (true)
            {
                string MarkHead(string text)
                {
                    var (tag, _) = markupStack.Pop();
                    if (useMetaTitle && tag == Markups.Meta2)
                    {
                        var colonIdx = text.IndexOf(":");
                        if (colonIdx != -1)
                        {
                            colonIdx++;
                            var metaTitle = Markups.MetaTitle;
                            text = $"<{metaTitle}>{text[0..colonIdx]}</{metaTitle}>{text[colonIdx..]}";
                        }
                    }
                    markupStack.Push((tag, true));
                    return text;
                }
                string TrimHead(string text)
                {
                    var (tag, textTrimmed) = markupStack.Count > 0 ? markupStack.Peek() : (null, false);
                    if (InlineTags.Contains(tag) || textTrimmed)
                        return text;
                    else if (tag == null)
                        return text.TrimStart();
                    else
                        return MarkHead(text.TrimStart());
                }
                void AppendTextContent(string text)
                {
                    if (markupStack.Count > 0)
                    {
                        var (tag, textProcessed) = markupStack.Pop();
                        if (!textProcessed && tag == Markups.Meta)
                        {
                            text = text.TrimStart();
                            textProcessed = true;
                            if (text.Length > 1)
                                text = char.ToUpper(text[0]) + text[1..];
                            else if (text.Length == 1)
                                text = text.ToUpper();
                        }
                        markupStack.Push((tag, textProcessed));
                    }
                    contentBuilder.Append(TrimHead(text));
                }
                var markupMatch = DictMarkupRegex.Match(content, currentPos);
                if (!markupMatch.Success)
                {
                    AppendTextContent(content[currentPos..]);
                    CloseTags();
                    break;
                }
                AppendTextContent(content[currentPos..markupMatch.Index]);
                currentPos = markupMatch.Index + markupMatch.Length;

                var rtfHeaderMatch = RtfHeaderMarkupRegex.Match(markupMatch.Value);
                if (rtfHeaderMatch.Success)
                {
                    var rtfLen = int.Parse(rtfHeaderMatch.Groups[1].Value);
                    var rtfContent = content.Substring(currentPos, rtfLen);
                    currentPos += rtfLen;
                    var htmlContent = Rtf.ToHtml(rtfContent);
                    contentBuilder.Append(htmlContent);
                }
                else if (markupMatch.Value == TAB)
                {
                    contentBuilder.Append($"<{Markups.Tab}></{Markups.Tab}>");
                }
                else if (markupMatch.Value == NEWLINE || markupMatch.Value == NEWLINE2)
                {
                    EnterTag("br", false);
                }
                else if (markupMatch.Value == META)
                {
                    CloseTags();
                    EnterTag(Markups.Meta);
                }
                else if (markupMatch.Value == DEF)
                {
                    CloseTags();
                    EnterTag(Markups.Definition);
                }
                else if (markupMatch.Value == EG)
                {
                    CloseTags();
                    EnterTag(Markups.Example);
                }
                else if (markupMatch.Value == EG_TSL)
                {
                    CloseTags();
                    EnterTag(Markups.ExampleTranslation);
                }
                else if (markupMatch.Value == REF)
                {
                    if (markupStack.Count > 0 && markupStack.Peek().tag == "a")
                        CloseTags(1);
                    else
                        EnterTag("a");
                }
                else if (markupMatch.Value == PRONO_OPEN)
                {
                    CloseTags();
                    EnterTag(Markups.PhoneticNotation);
                }
                else if (markupMatch.Value == PRONO_CLOSE)
                {
                    CloseTags();
                }
                else if (markupMatch.Value == IDIOM)
                {
                    CloseTags();
                    EnterTag(Markups.Idiom);
                }
                else if (markupMatch.Value == IDIOM_TSL)
                {
                    CloseTags();
                    EnterTag(Markups.IdiomTranslation);
                }
                else if (markupMatch.Value == IDIOM_EG)
                {
                    CloseTags();
                    EnterTag(Markups.IdiomExample);
                }
                else if (markupMatch.Value == IDIOM_EG_TSL)
                {
                    CloseTags();
                    EnterTag(Markups.IdiomExampleTranslation);
                }
                else if (markupMatch.Value == DEF2)
                {
                    CloseTags();
                    EnterTag(Markups.Meta2);
                }
                else if (markupMatch.Value == ALT)
                {
                    CloseTags();
                    EnterTag(Markups.Alternative);
                }
                else if (markupMatch.Value == MEDIA_OPEN)
                {
                    CloseTags();
                    EnterTag(Markups.Media);
                }
                else if (markupMatch.Value == MEDIA_CLOSE)
                {
                    CloseTags();
                }
            }
            content = contentBuilder.ToString();
            content = RefTagRegex.Replace(content, m => {
                doc.LoadHtml(m.Groups[2].Value);
                var keyword = Tools.UrlEncodeMinimal(doc.DocumentNode.InnerText);
                var innerHtml = m.Groups[2].Value;
                return $"{m.Groups[1].Value} href=\"{keyword}\">{innerHtml}</a>";
            });
            if (content.Any(chr =>
                char.IsControl(chr) && chr != '\r' && chr != '\n' || chr == '\x81' || chr == '\x8D' || chr == '\x8F' || chr == '\x90' || chr == '\x9D'))
                errorMessages = new[] { "Content has garbage characters" };
            return (content, errorMessages);
        }

        public static (string content, string[] errorMessages) Resolve(string content,
            bool useMetaTitle = false, bool fixBulletPoint = false,
            bool useEastAsianFont = false, Regex chinesePrefixedListRegex = null)
        {
            var errorMessages = Array.Empty<string>();

            if (content.StartsWith(Patching.AsIsTag))
            {
                content = content[Patching.AsIsTag.Length..];
                goto SKIP_HTML_PARSING;
            }

            content = FauxTagRegex.Replace(content, m => {
                if (m.Value == "<super>")
                    return "&lt;super&gt;";
                if (m.Value == "<subs>")
                    return "&lt;subs&gt;";
                return m.Value;
            });

            void AddCloseTagForEncodingTag()
            {
                var corrections = new List<(int pos, string code)>();
                var currentPos = 0;
                var needToAddLastCloseTag = false;
                var lastCode = default(string);
                while (true)
                {
                    var openMatch = CodeOpenTagRegex.Match(content, currentPos);
                    if (!openMatch.Success)
                        break;
                    currentPos = openMatch.Index + openMatch.Length;
                    var openCode = lastCode = openMatch.Groups[1].Value;
                    needToAddLastCloseTag = true;

                    var endMatch = CodeCloseOrOpenTagRegex.Match(content, currentPos);
                    if (!endMatch.Success)
                        break;
                    currentPos = endMatch.Index;
                    needToAddLastCloseTag = false;

                    var nextCode = endMatch.Groups[1].Value;
                    var nextTag = endMatch.Value;

                    if (!(nextTag[1] == '/' && nextCode == openCode))
                    {
                        corrections.Add((currentPos, openCode));
                        continue;
                    }

                    currentPos = endMatch.Index + endMatch.Length;
                }
                if (needToAddLastCloseTag)
                    corrections.Add((content.Length, lastCode));

                if (corrections.Count > 0)
                {
                    var strBuilder = new StringBuilder(content);
                    foreach (var (pos, code) in corrections.AsEnumerable().Reverse())
                        strBuilder.Insert(pos, $"</c{code}>");

                    content = strBuilder.ToString();
                }
            }
            AddCloseTagForEncodingTag();

            doc.LoadHtml(content);

            var parseErrors = doc.ParseErrors
                .Where(e => e.Code != HtmlParseErrorCode.TagNotOpened &&
                            e.Code != HtmlParseErrorCode.EndTagNotRequired &&
                            e.Code != HtmlParseErrorCode.TagNotClosed)
                .ToArray();
            if (parseErrors.Length > 0)
                errorMessages = parseErrors
                    .Select(e => $"{e.Code} (Line: {e.Line}, LinePosition: {e.LinePosition}, Reason: {e.Reason}).")
                    .ToArray();

            bool IsTextNode(HtmlNode node)
                => node.NodeType == HtmlNodeType.Text;

            bool IsElementNode(HtmlNode node)
                => node.NodeType == HtmlNodeType.Element;

            bool IsEncodingNode(HtmlNode node)
                => IsElementNode(node) && node.Name.Length > 2 && char.ToLower(node.Name[0]) == 'c';

            var remainingTextNodes = new HashSet<HtmlTextNode>();
            var pendingUnwrappedNodes = new List<HtmlNode>();
            var pendingEastAsianNodes = new List<HtmlNode>();
            var pendingRemovedNodes = new List<HtmlNode>();
            var pendingRenamedNodes = new List<HtmlNode>();
            void TraverseDomAndStageOps(HtmlNode node)
            {
                foreach (var childNode in node.ChildNodes)
                    TraverseDomAndStageOps(childNode);
                if (IsTextNode(node))
                {
                    remainingTextNodes.Add(node as HtmlTextNode);
                }
                else if (IsElementNode(node) && TagsToUnwrap.Contains(node.Name))
                {
                    pendingUnwrappedNodes.Add(node);
                }
                else if (IsElementNode(node) && TagsToRemove.Contains(node.Name))
                {
                    pendingRemovedNodes.Add(node);
                }
                else if (IsElementNode(node) && TagNameMap.Select(e => e.Item1).Contains(node.Name))
                {
                    pendingRenamedNodes.Add(node);
                }
                else if (IsEncodingNode(node))
                {
                    var realEncoding = default(Encoding);
                    try { realEncoding = Encoding.GetEncoding(int.Parse(node.Name[1..])); }
                    catch (FormatException) { return; }

                    void ResolveTextEncodingAndStageOps(HtmlNode node)
                    {
                        foreach (var childNode in node.ChildNodes)
                            if (!IsEncodingNode(childNode))
                                ResolveTextEncodingAndStageOps(childNode);
                        if (node.NodeType != HtmlNodeType.Text)
                            return;
                        var textNode = node as HtmlTextNode;
                        remainingTextNodes.Remove(textNode);
                        textNode.Text = realEncoding
                            .GetString(Encoding.Latin1.GetBytes(textNode.Text))
                            .Normalize();
                    }
                    ResolveTextEncodingAndStageOps(node);
                    if (useEastAsianFont && EastAsianLangs.Any(lang => realEncoding.EncodingName.StartsWith(lang)))
                        pendingEastAsianNodes.Add(node);
                    else
                        pendingUnwrappedNodes.Add(node);
                }
            }
            TraverseDomAndStageOps(doc.DocumentNode);

            foreach (var textNode in remainingTextNodes)
                textNode.Text = Encoding.GetEncoding(1258)
                    .GetString(Encoding.Latin1.GetBytes(textNode.Text))
                    .Normalize();
            foreach (var node in pendingEastAsianNodes)
            {
                node.Name = "span";
                node.AddClass(Markups.EastAsianTextClass);
            }
            foreach (var node in pendingUnwrappedNodes)
            {
                if (node.Name == "usage")
                {
                    var first = node.ChildNodes.FirstOrDefault() as HtmlTextNode;
                    var last = node.ChildNodes.LastOrDefault() as HtmlTextNode;
                    if (first?.Text[0] == '(')
                        first.Text = '〈' + first.Text[1..];
                    if (last?.Text[0] == '>')
                        last.Text = last.Text[0..^1] + '〉';
                }
                node.ParentNode.RemoveChild(node, true);
            }
            foreach (var node in pendingRemovedNodes)
                node.Remove();
            foreach (var node in pendingRenamedNodes)
                node.Name = TagNameMap.First(e => e.Item1 == node.Name).Item2;

            content = doc.DocumentNode.OuterHtml;

            content = FauxTagAndGlitchRegex2.Replace(content, m => {
                if (m.Value.StartsWith("<super>") && m.Value.Length > "<super>".Length)
                    return "<sup>" + m.Value["<super>".Length..] + "</sup>";
                if (m.Value.StartsWith("<subs>") && m.Value.Length > "<subs>".Length)
                    return "<sub>" + m.Value["<subs>".Length..] + "</sub>";
                if (m.Value.First() == '(' && m.Value.Last() == '>')
                    return ';' + m.Value[1..^1] + ';';
                return "";
            });

        SKIP_HTML_PARSING:
            var errorMessages2 = default(string[]);
            (content, errorMessages2) = ResolveLactVietDictMarkups(content,
                useMetaTitle: useMetaTitle,
                fixBulletPoint: fixBulletPoint
            );

            if (chinesePrefixedListRegex != null)
            {
                content = chinesePrefixedListRegex.Replace(content, m => {
                    doc.LoadHtml(m.Groups[2].Value);
                    var xpath = ".//br[position() > 1][not(self::node()[not(following-sibling::*)])]";
                    foreach (var br in doc.DocumentNode.SelectNodes(xpath)?.ToArray() ?? Array.Empty<HtmlNode>())
                    {
                        var commaSpan = doc.CreateElement("span");
                        commaSpan.AddClass(Markups.EastAsianTextClass);
                        commaSpan.InnerHtml = "、";
                        br.ParentNode.ReplaceChild(commaSpan, br);
                    }
                    var lastBr = doc.DocumentNode.SelectSingleNode(".//br[last()]");
                    if (lastBr != null)
                        lastBr.Remove();
                    return string.Concat(m.Groups[1].Value, doc.DocumentNode.InnerHtml, m.Groups[3].Value);
                });
            }

            return (content, errorMessages.Concat(errorMessages2).ToArray());
        }

        static readonly string[] BoolChrs = new[] { "x", "v", "y", "n" };
        static readonly string[] TruthyChrs = new[] { "v", "y" };
        static readonly string[] FalsyChrs = new[] { "x", "n" };
        static readonly Regex BlankLineRegex = new(@"^\s+$", RegexOptions.Multiline | RegexOptions.Compiled);
        static readonly Regex AffirmNegateRegex = new(@"(?<=[(:;,‘<]\s*)\w+(?=\s*[>)’])", RegexOptions.Compiled);
        static readonly Regex AffirmNegateRegex2 = new(@"✔+|✘+", RegexOptions.Compiled);
        static readonly Regex ExampleAnswerRegex = new("<(.+?)>", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex SectionRegex = new(@"^<\$(?<text>.+?)>(?<rest>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex SingleChoiceRegex =
            new(@"^(?:(?<pre>.*?)<\s*(?<blank>(?:_+?\s*[^<>]+?\s*_*?)|(?:_*?\s*[^<>]+?\s*_+?))\s*\|@\|(?<answers>.+?)>)+(?<rest>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex MultipleChoiceRegex =
            new(@"^(?:(?<pre>.*?)<\s*(?<blank>_+)\s*\|(?<choices>[^<>]+?)>)+(?<rest>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);
        const string ExampleTitle = "Vi du:";
        public static string ResolveExercise(string rawContent)
        {
            rawContent = BlankLineRegex.Replace(rawContent, "");
            static string ReplaceAffirmNegate(string input) => AffirmNegateRegex.Replace(input, m => {
                if (m.Value.All(e => TruthyChrs.Contains(char.ToLower(e).ToString())))
                    return new string('✔', m.Value.Length);
                else if (m.Value.All(e => FalsyChrs.Contains(char.ToLower(e).ToString())))
                    return new string('✘', m.Value.Length);
                return m.Value;
            });
            static string ReplaceBold(string input, bool useSplit = false) => ExampleAnswerRegex.Replace(input, m => {
                if (useSplit)
                    return string.Join(" / ", m.Groups[1].Value.Split('|').Select(e => $"<b>{e.Trim()}</b>"));
                else
                    return $"<b>{m.Groups[1].Value.Trim()}</b>";
            });
            static bool IsExampleLine(string txt)
                => Tools.RemoveDiacritics(txt[0..ExampleTitle.Length]).StartsWith(ExampleTitle);

            var lines = rawContent
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimStart('\uFEFF'))
                .Where(line => line.Length > 0);

            var outputBuilder = new StringBuilder();

            var issueName = 0;
            foreach (var baseLine in lines)
            {
                var line = baseLine;
                var gotoHasBeenDone = false;
            LOOP_START:
                issueName++;
                Match m;
                if (!gotoHasBeenDone && (m = SectionRegex.Match(line)).Success)
                {
                    var text = ReplaceBold(ReplaceAffirmNegate(m.Groups["text"].Value + m.Groups["rest"].Value));
                    outputBuilder.Append($"<h4>{text}</h4>");
                }
                else if (!gotoHasBeenDone && IsExampleLine(line))
                {
                    var examples = line[ExampleTitle.Length..]
                        .Split("\n", StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim().TrimStart('\uFEFF'))
                        .Where(line => line.Length > 0);

                    outputBuilder.Append("<p class=\"eg-title\">Ví dụ:</p><ul class=\"eg-list\">");

                    foreach (var example in examples)
                    {
                        var modExample = ReplaceBold(ReplaceAffirmNegate(example), useSplit: true);
                        outputBuilder.Append($"<li>{modExample}</li>");
                    }

                    outputBuilder.Append("</ul>");
                }
                // some questions has "semi-blank" fields, each has an existing word and you have to type words around it.
                else if ((m = SingleChoiceRegex.Match(line)).Success)
                {
                    var groups = m.Groups["pre"].Captures.Select(e => ReplaceBold(ReplaceAffirmNegate(e.Value)))
                        .Zip(m.Groups["blank"].Captures.Select(e => e.Value))
                        .Zip(m.Groups["answers"].Captures.Select(e =>
                            e.Value.Split('|')
                                .Select(s => ReplaceBold(ReplaceAffirmNegate(s).Trim()))))
                        .Select(e => (e.First.First, e.First.Second, e.Second));

                    if (!gotoHasBeenDone)
                        outputBuilder.Append("<p>");

                    foreach (var (pre, blank, answers) in groups)
                    {
                        // #: leave blank as an answer
                        var modBlank = blank;
                        if (modBlank.StartsWith("_"))
                            modBlank = "_____" + modBlank.TrimStart('_');
                        if (modBlank.Length != "_____".Length && modBlank.EndsWith("_"))
                            modBlank = modBlank.TrimEnd('_') + "____";
                        var preText = pre.Replace("\n", "<br>");
                        outputBuilder.Append(preText)
                            .Append(!modBlank.StartsWith('_') || !modBlank.EndsWith('_') ? $" {modBlank} " : "")
                            .Append("<input type=\"text\">")
                            .Append($" <d-answers>({string.Join('/', answers.Select(e => $"<d-answer>{e}</d-answer>"))})</d-answers>");
                    }

                    var rest = m.Groups["rest"].Value.Trim();
                    if (rest.Length > 0)
                    {
                        line = rest;
                        gotoHasBeenDone = true;
                        goto LOOP_START;
                    }

                    outputBuilder.Append("</p>");
                }
                else if ((m = MultipleChoiceRegex.Match(line)).Success)
                {
                    var groups = m.Groups["pre"].Captures.Select(e => ReplaceBold(ReplaceAffirmNegate(e.Value)))
                        .Zip(m.Groups["blank"].Captures.Select(e => e.Value))
                        .Zip(m.Groups["choices"].Captures.Select(e =>
                            e.Value.Split('|').Select(s =>
                                ReplaceBold(ReplaceAffirmNegate(s).Trim()))).ToArray())
                        .Select(e => (e.First.First, e.First.Second, e.Second));

                    if (!gotoHasBeenDone)
                        outputBuilder.Append("<p>");

                    foreach (var (pre, blank, choices) in groups)
                    {
                        var preText = pre.Replace("\n", "<br>");
                        var answer = choices.First(e => e.StartsWith("*"))?.TrimStart('*');
                        var modChoices = choices.Select(e => e.TrimStart('*')).ToArray().AsEnumerable();
                        if (modChoices.All(ch => BoolChrs.Contains(ch.ToLower())))
                            modChoices = modChoices.Select(ch => TruthyChrs.Contains(ch.ToLower()) ? "✔" : "✘");
                        var choiceSeq = string.Join('/', modChoices
                            .Select(e =>
                                $"<d-choice " +
                                    (e == answer ? $"class=\"answer\" " : "") +
                                    $"name=\"iss-{issueName}\">" +
                                    $"{e}<d-oval></d-oval>" +
                                $"</d-choice>"
                            )
                        );
                        var modBlank = blank;
                        if (modBlank.StartsWith("_"))
                            modBlank = "_____" + modBlank.TrimStart('_');
                        if (modBlank.Length != "_____".Length && modBlank.EndsWith("_"))
                            modBlank = modBlank.TrimEnd('_') + "____";
                        outputBuilder.Append($"{preText} {modBlank} ({choiceSeq}) ");
                        issueName++;
                    }

                    var rest = m.Groups["rest"].Value.Trim();
                    if (rest.Length > 0)
                    {
                        line = rest;
                        gotoHasBeenDone = true;
                        goto LOOP_START;
                    }

                    outputBuilder.Append("</p>");
                }
                else
                {
                    var example = ReplaceBold(ReplaceAffirmNegate(line), useSplit: true).Replace("\n", "<br>");
                    if (gotoHasBeenDone)
                        outputBuilder.Append($"{example}</p>");
                    else
                        outputBuilder.Append($"<p>{example}</p>");
                }
            }

            return AffirmNegateRegex2.Replace(outputBuilder.ToString(), m => {
                if (m.Value[0] == '✔')
                    return $"<d-tick>{m.Value}</d-tick>";
                else if (m.Value[0] == '✘')
                    return $"<d-cross>{m.Value}</d-cross>";
                return m.Value;
            });
        }
    }
}

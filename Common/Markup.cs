using HtmlAgilityPack;
using RtfPipe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        const string CAT = "**";
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
        static readonly Regex DictMarkupRegex = new(string.Join('|', new[] { CAT, DEF, EG, EG_TSL, REF, PRONO_OPEN, PRONO_CLOSE, IDIOM, IDIOM_TSL, IDIOM_EG, IDIOM_EG_TSL, DEF2, ALT, MEDIA_OPEN, MEDIA_CLOSE, NEWLINE, NEWLINE2, TAB, FF }.Select(e => Regex.Escape(e))) + $"|{RTF}", RegexOptions.Compiled);
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
                var markupMatch = DictMarkupRegex.Match(content, currentPos);
                if (!markupMatch.Success)
                {
                    contentBuilder.Append(TrimHead(content[currentPos..]));
                    CloseTags();
                    break;
                }
                contentBuilder.Append(TrimHead(content[currentPos..markupMatch.Index]));
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
                else if (markupMatch.Value == CAT)
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

        public static (string content, string[] errorMessages) Resolve(
            string content, bool useMetaTitle = false, bool fixBulletPoint = false, bool useEastAsianFont = false)
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

            return (content, errorMessages.Concat(errorMessages2).ToArray());
        }
    }
}

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace BlazorApp1.Services;

public static class TextFormatter
{
    public static MarkupString ToFormattedHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return (MarkupString)string.Empty;
        }

        // 1. Separate code blocks to protect them during markdown parsing
        var codeBlocks = new List<string>();
        var text = Regex.Replace(markdown.Trim(), @"```(?:[a-zA-Z0-9_-]+)?\r?\n([\s\S]*?)```", m =>
        {
            var code = WebUtility.HtmlEncode(m.Groups[1].Value.Trim());
            var placeholder = $"__CODE_BLOCK_{codeBlocks.Count}__";
            codeBlocks.Add($"<pre class=\"my-2.5 p-3 rounded-lg bg-[#070a10] border border-line text-xs font-mono overflow-x-auto text-ink-secondary leading-relaxed\"><code>{code}</code></pre>");
            return placeholder;
        });

        // 2. HTML encode everything else for security
        text = WebUtility.HtmlEncode(text);

        // 3. Inline code `code`
        text = Regex.Replace(text, @"`([^`\n]+)`", "<code class=\"px-1.5 py-0.5 rounded bg-[#070a10] border border-line text-[0.785rem] font-mono text-accent\">$1</code>");

        // 4. Passage citations like [Passage 1, page 1], [Passage 2, p. 3], [p. 1]
        text = Regex.Replace(text, @"\[(Passage\s+\d+[^\]]*|p\.\s*\d+[^\]]*|Citation\s*#?\d+[^\]]*)\]", 
            "<span class=\"inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-accent/15 border border-accent/30 text-accent text-[0.725rem] font-medium font-mono shadow-xs mx-0.5 align-baseline\"><svg class=\"w-3 h-3 text-accent shrink-0\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.2\"><path d=\"M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z\"/><path d=\"M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z\"/></svg>$1</span>");

        // 5. Bold & Italic
        text = Regex.Replace(text, @"\*\*([^\*\n]+)\*\*", "<strong class=\"font-semibold text-ink\">$1</strong>");
        text = Regex.Replace(text, @"__([^_\n]+)__", "<strong class=\"font-semibold text-ink\">$1</strong>");
        text = Regex.Replace(text, @"\*([^\*\n]+)\*", "<em class=\"italic text-ink-secondary\">$1</em>");

        // 6. Blockquotes
        text = Regex.Replace(text, @"(?m)^&gt;\s*(.*?)$", "<blockquote class=\"border-l-2 border-accent pl-3 py-1 my-2 text-ink-secondary text-[0.825rem] italic bg-accent/5 rounded-r\">$1</blockquote>");

        // 7. Parse line-by-line into structured semantic blocks (Headings, Lists, Paragraphs)
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();
        bool inUnorderedList = false;
        bool inOrderedList = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                if (inUnorderedList) { sb.Append("</ul>"); inUnorderedList = false; }
                if (inOrderedList) { sb.Append("</ol>"); inOrderedList = false; }
                continue;
            }

            // Headings ###, ##, #
            var h3Match = Regex.Match(line, @"^###\s+(.*)$");
            if (h3Match.Success)
            {
                if (inUnorderedList) { sb.Append("</ul>"); inUnorderedList = false; }
                if (inOrderedList) { sb.Append("</ol>"); inOrderedList = false; }
                sb.Append($"<h4 class=\"text-[0.925rem] font-bold text-ink mt-3 mb-1.5 flex items-center gap-1.5\">{h3Match.Groups[1].Value}</h4>");
                continue;
            }

            var h2Match = Regex.Match(line, @"^##\s+(.*)$");
            if (h2Match.Success)
            {
                if (inUnorderedList) { sb.Append("</ul>"); inUnorderedList = false; }
                if (inOrderedList) { sb.Append("</ol>"); inOrderedList = false; }
                sb.Append($"<h3 class=\"text-[1rem] font-bold text-ink mt-3.5 mb-1.5 border-b border-line pb-1\">{h2Match.Groups[1].Value}</h3>");
                continue;
            }

            var h1Match = Regex.Match(line, @"^#\s+(.*)$");
            if (h1Match.Success)
            {
                if (inUnorderedList) { sb.Append("</ul>"); inUnorderedList = false; }
                if (inOrderedList) { sb.Append("</ol>"); inOrderedList = false; }
                sb.Append($"<h2 class=\"text-[1.075rem] font-bold text-ink mt-4 mb-2\">{h1Match.Groups[1].Value}</h2>");
                continue;
            }

            // Unordered list item (- item or * item)
            var ulMatch = Regex.Match(line, @"^[\-\*]\s+(.*)$");
            if (ulMatch.Success)
            {
                if (inOrderedList) { sb.Append("</ol>"); inOrderedList = false; }
                if (!inUnorderedList)
                {
                    sb.Append("<ul class=\"my-2 space-y-1.5 pl-4 list-disc marker:text-accent text-[0.85rem]\">");
                    inUnorderedList = true;
                }
                sb.Append($"<li class=\"leading-relaxed text-ink/90 pl-1\">{ulMatch.Groups[1].Value}</li>");
                continue;
            }

            // Ordered list item (1. item)
            var olMatch = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
            if (olMatch.Success)
            {
                if (inUnorderedList) { sb.Append("</ul>"); inUnorderedList = false; }
                if (!inOrderedList)
                {
                    sb.Append("<ol class=\"my-2 space-y-1.5 pl-4 list-decimal marker:text-accent font-medium text-[0.85rem]\">");
                    inOrderedList = true;
                }
                sb.Append($"<li class=\"leading-relaxed text-ink/90 pl-1\"><span class=\"font-normal\">{olMatch.Groups[2].Value}</span></li>");
                continue;
            }

            // Normal paragraph text
            if (inUnorderedList) { sb.Append("</ul>"); inUnorderedList = false; }
            if (inOrderedList) { sb.Append("</ol>"); inOrderedList = false; }

            sb.Append($"<p class=\"mb-2 leading-relaxed text-ink/90 text-[0.85rem]\">{line}</p>");
        }

        if (inUnorderedList) sb.Append("</ul>");
        if (inOrderedList) sb.Append("</ol>");

        var result = sb.ToString();

        // Restore code blocks
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            result = result.Replace($"__CODE_BLOCK_{i}__", codeBlocks[i]);
            result = result.Replace($"<p class=\"mb-2 leading-relaxed text-ink/90 text-[0.85rem]\">__CODE_BLOCK_{i}__</p>", codeBlocks[i]);
        }

        return (MarkupString)result;
    }
}

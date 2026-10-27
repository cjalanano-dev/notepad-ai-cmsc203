using System.Net;
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

        // 1. HTML Encode raw text to prevent XSS
        var encoded = WebUtility.HtmlEncode(markdown.Trim());

        // 2. Code blocks ```lang\ncode\n```
        encoded = Regex.Replace(encoded, @"```(?:[a-zA-Z0-9_-]+)?\r?\n([\s\S]*?)```", m =>
        {
            var code = m.Groups[1].Value.Trim();
            return $"<pre class=\"my-2.5 p-3 rounded-lg bg-[#070a10] border border-line text-xs font-mono overflow-x-auto text-ink-secondary leading-relaxed\"><code>{code}</code></pre>";
        });

        // 3. Inline code `code`
        encoded = Regex.Replace(encoded, @"`([^`\n]+)`", "<code class=\"px-1.5 py-0.5 rounded bg-[#070a10] border border-line text-[0.785rem] font-mono text-accent\">$1</code>");

        // 4. Blockquotes > quote
        encoded = Regex.Replace(encoded, @"(?m)^&gt;\s*(.*?)$", "<blockquote class=\"border-l-2 border-accent pl-3 py-1 my-2 text-ink-secondary text-[0.825rem] italic bg-accent-soft/30 rounded-r\">$1</blockquote>");

        // 5. Bold **text** or __text__
        encoded = Regex.Replace(encoded, @"\*\*([^\*\n]+)\*\*", "<strong class=\"font-semibold text-ink\">$1</strong>");
        encoded = Regex.Replace(encoded, @"__([^_\n]+)__", "<strong class=\"font-semibold text-ink\">$1</strong>");

        // 6. Italic *text* or _text_
        encoded = Regex.Replace(encoded, @"\*([^\*\n]+)\*", "<em class=\"italic text-ink-secondary\">$1</em>");
        encoded = Regex.Replace(encoded, @"(?<!\w)_([^_\n]+)_(?!\w)", "<em class=\"italic text-ink-secondary\">$1</em>");

        // 7. Passage citations like [Passage 1, page 1] or [p. 1] or [Passage 2, p. 3]
        encoded = Regex.Replace(encoded, @"\[(Passage\s+\d+[^\]]*|p\.\s*\d+[^\]]*)\]", "<span class=\"inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-surface-raised border border-accent/30 text-accent text-[0.725rem] font-medium font-mono mx-0.5\">$1</span>");

        // 8. Headers # ## ###
        encoded = Regex.Replace(encoded, @"(?m)^###\s+(.*?)$", "<h4 class=\"text-[0.95rem] font-bold text-ink mt-3 mb-1.5\">$1</h4>");
        encoded = Regex.Replace(encoded, @"(?m)^##\s+(.*?)$", "<h3 class=\"text-[1.05rem] font-bold text-ink mt-3.5 mb-1.5\">$1</h3>");
        encoded = Regex.Replace(encoded, @"(?m)^#\s+(.*?)$", "<h2 class=\"text-[1.15rem] font-bold text-ink mt-4 mb-2\">$1</h2>");

        // 9. Unordered list items - item or * item
        encoded = Regex.Replace(encoded, @"(?m)^[\-\*]\s+(.*?)$", "<li class=\"ml-4 list-disc text-ink-secondary leading-relaxed\">$1</li>");

        // 10. Numbered list items 1. item
        encoded = Regex.Replace(encoded, @"(?m)^(\d+)\.\s+(.*?)$", "<li class=\"ml-4 list-decimal text-ink-secondary leading-relaxed\"><span class=\"text-ink\">$2</span></li>");

        // 11. Convert double line breaks into paragraphs or clean spacing
        encoded = Regex.Replace(encoded, @"\r?\n\r?\n", "<br class=\"my-2\"/><br/>");
        encoded = Regex.Replace(encoded, @"\r?\n", "<br/>");

        return (MarkupString)encoded;
    }
}

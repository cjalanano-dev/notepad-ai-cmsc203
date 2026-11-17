using System.Text.RegularExpressions;
using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class DocumentService
{
    public const int ChunkSize = 1200;
    public const int ChunkOverlap = 150;

    // Regex patterns for institutional boilerplate, header/footer artifacts, professor credentials, copyright, and grading rubrics
    private static readonly Regex[] AcademicNoisePatterns = new[]
    {
        // University / College / Department names
        new Regex(@"(?i)\b(university|college|department|faculty|school\s+of|institute\s+of|academy)\b.*?(technology|science|engineering|computing|arts|education|studies)?", RegexOptions.Compiled),
        
        // Course codes / Section / Term headers
        new Regex(@"(?i)\b(cmsc|cs|it|cpe|eng|math|stat|phys|chem|bio)\s*[-_]?\s*\d{2,4}\b.*?(section|sec|term|semester|ay|sy\s*\d{4})?", RegexOptions.Compiled),
        
        // Instructors / Professors / Authors / Proponents
        new Regex(@"(?i)\b(instructor|professor|prof\.|engr\.|dr\.|faculty\s+member|prepared\s+by|submitted\s+by|proponents?|author|presented\s+to|adviser)\b.*", RegexOptions.Compiled),
        
        // Document metadata, slide markers, dates & copyright
        new Regex(@"(?i)\b(all\s+rights\s+reserved|copyright\s*©?|\(c\)\s*\d{4}|confidential|for\s+internal\s+use|reproduction\s+prohibited)\b.*", RegexOptions.Compiled),
        new Regex(@"(?i)^(page|slide|p\.|pg\.)\s*\d+\s*(of\s*\d+)?$", RegexOptions.Compiled),
        new Regex(@"(?i)^(module\s+\d+|chapter\s+\d+|unit\s+\d+|lesson\s+\d+)\s*[:\-_]?\s*$", RegexOptions.Compiled),
        new Regex(@"(?i)\b(date\s+submitted|due\s+date|academic\s+year|first\s+semester|second\s+semester|midterm|prelim|finals)\b.*", RegexOptions.Compiled),
        
        // Learning objectives, outcomes, goals, grading criteria, and pedagogical boilerplate
        new Regex(@"(?i)\b(learning\s+objectives?|course\s+objectives?|intended\s+learning\s+outcomes?|ilos?|intended\s+outcomes?|lesson\s+objectives?|module\s+objectives?|at\s+the\s+end\s+of\s+this\s+(lesson|chapter|module|session)|students\s+will\s+be\s+able\s+to|by\s+the\s+end\s+of|expected\s+outcomes?)\b.*", RegexOptions.Compiled),
        new Regex(@"(?i)\b(grading\s+system|rubrics?|grading\s+criteria|assessment\s+tasks?|house\s+rules|class\s+policies)\b.*", RegexOptions.Compiled)
    };

    public List<(int PageNumber, string Content)> ExtractText(string filename, string textContent)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ExtractTextFromTxt(textContent);
    }

    public List<(int PageNumber, string Content)> ExtractTextFromTxt(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new();

        // Check if content already contains page markers e.g. "--- Page X ---"
        var pageMatches = Regex.Split(content, @"(?m)^---\s*Page\s+(\d+)\s*---\r?\n?");
        if (pageMatches.Length > 1)
        {
            var pList = new List<(int PageNumber, string Content)>();
            for (int i = 1; i < pageMatches.Length; i += 2)
            {
                if (int.TryParse(pageMatches[i], out var pNum))
                {
                    var pageBody = (i + 1 < pageMatches.Length) ? pageMatches[i + 1] : "";
                    var cleanedPage = SanitizePageText(pageBody);
                    if (!string.IsNullOrWhiteSpace(cleanedPage))
                    {
                        pList.Add((pNum, cleanedPage));
                    }
                }
            }
            if (pList.Count > 0) return pList;
        }

        const int pseudoPageSize = 2000;
        var results = new List<(int PageNumber, string Content)>();
        var pageNum = 1;

        for (int i = 0; i < content.Length; i += pseudoPageSize)
        {
            var len = Math.Min(pseudoPageSize, content.Length - i);
            var pageText = content.Substring(i, len);
            var sanitized = SanitizePageText(pageText);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                results.Add((pageNum++, sanitized));
            }
        }

        return results;
    }

    /// <summary>
    /// Intelligently purges institutional headers, course logistics, professor titles, 
    /// slide counters, and repetitive boilerplate, retaining core educational content.
    /// </summary>
    public static string SanitizePageText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var cleanLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Strip explicit standalone page headers / footers
            if (Regex.IsMatch(line, @"^(---|\*\*\*|===)?\s*(Page|Slide|Passage)\s*\d+.*$", RegexOptions.IgnoreCase))
                continue;

            // Check against academic noise patterns
            bool isNoise = false;
            foreach (var pattern in AcademicNoisePatterns)
            {
                if (pattern.IsMatch(line))
                {
                    // Allow if the line also discusses significant technical keywords e.g. "Computer Science: Linked List data structures"
                    if (Regex.IsMatch(line, @"\b(data\s+structure|algorithm|method|function|class|object|pointer|variable|complexity|database|query|memory|theorem|property)\b", RegexOptions.IgnoreCase))
                    {
                        isNoise = false;
                    }
                    else
                    {
                        isNoise = true;
                    }
                    break;
                }
            }

            if (!isNoise)
            {
                cleanLines.Add(line);
            }
        }

        return string.Join("\n", cleanLines).Trim();
    }

    public List<SourceChunkItem> ChunkText(int pageNum, string text, int startingChunkIndex = 0)
    {
        var paragraphs = Regex.Split(text, @"\r?\n\s*\r?\n");
        var chunks = new List<string>();
        var currentChunk = "";

        foreach (var p in paragraphs)
        {
            var cleanedPara = p.Trim();
            if (string.IsNullOrEmpty(cleanedPara)) continue;

            if (currentChunk.Length + cleanedPara.Length + 2 <= ChunkSize)
            {
                currentChunk = string.IsNullOrEmpty(currentChunk) ? cleanedPara : $"{currentChunk}\n\n{cleanedPara}";
            }
            else
            {
                if (!string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk);
                }

                if (cleanedPara.Length > ChunkSize)
                {
                    int start = 0;
                    while (start < cleanedPara.Length)
                    {
                        int end = Math.Min(start + ChunkSize, cleanedPara.Length);
                        string subStr = cleanedPara.Substring(start, end - start).Trim();
                        if (!string.IsNullOrEmpty(subStr))
                        {
                            chunks.Add(subStr);
                        }
                        start += (ChunkSize - ChunkOverlap);
                    }
                    currentChunk = "";
                }
                else
                {
                    currentChunk = cleanedPara;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentChunk))
        {
            chunks.Add(currentChunk);
        }

        var result = new List<SourceChunkItem>();
        int idx = startingChunkIndex;
        foreach (var c in chunks)
        {
            if (c.Trim().Length > 30)
            {
                result.Add(new SourceChunkItem
                {
                    Id = idx + 1,
                    PageNumber = pageNum,
                    ChunkIndex = idx,
                    Content = c
                });
                idx++;
            }
        }

        return result;
    }

    public List<SourceChunkItem> ProcessDocumentText(string filename, string rawText)
    {
        var pages = ExtractText(filename, rawText);
        var allChunks = new List<SourceChunkItem>();
        int chunkIdx = 0;

        foreach (var page in pages)
        {
            var pageChunks = ChunkText(page.PageNumber, page.Content, chunkIdx);
            foreach (var c in pageChunks)
            {
                c.DocumentFilename = filename;
                allChunks.Add(c);
                chunkIdx++;
            }
        }

        return allChunks;
    }
}

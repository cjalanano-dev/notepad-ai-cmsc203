using System.Text.RegularExpressions;
using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class DocumentService
{
    public const int ChunkSize = 1200;
    public const int ChunkOverlap = 150;

    public List<(int PageNumber, string Content)> ExtractText(string filename, string textContent)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext == ".txt" || ext == ".md" || string.IsNullOrEmpty(ext))
        {
            return ExtractTextFromTxt(textContent);
        }

        // For pdf or other text representation
        return ExtractTextFromTxt(textContent);
    }

    public List<(int PageNumber, string Content)> ExtractTextFromTxt(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new();

        const int pseudoPageSize = 2000;
        var results = new List<(int PageNumber, string Content)>();
        var pageNum = 1;

        for (int i = 0; i < content.Length; i += pseudoPageSize)
        {
            var len = Math.Min(pseudoPageSize, content.Length - i);
            var pageText = content.Substring(i, len).Trim();
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                results.Add((pageNum++, pageText));
            }
        }

        return results;
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

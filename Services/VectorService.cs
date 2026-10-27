using System.Text.RegularExpressions;
using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class VectorService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "aren't",
        "as", "at", "be", "because", "been", "before", "being", "below", "between", "both", "but", "by",
        "can't", "cannot", "could", "couldn't", "did", "didn't", "do", "does", "doesn't", "doing", "don't",
        "down", "during", "each", "few", "for", "from", "further", "had", "hadn't", "has", "hasn't", "have",
        "haven't", "having", "he", "he'd", "he'll", "he's", "her", "here", "here's", "hers", "herself",
        "him", "himself", "his", "how", "how's", "i", "i'd", "i'll", "i'm", "i've", "if", "in", "into",
        "is", "isn't", "it", "it's", "its", "itself", "let's", "me", "more", "most", "mustn't", "my",
        "myself", "no", "nor", "not", "of", "off", "on", "once", "only", "or", "other", "ought", "our",
        "ours", "ourselves", "out", "over", "own", "same", "shan't", "she", "she'd", "she'll", "she's",
        "should", "shouldn't", "so", "some", "such", "than", "that", "that's", "the", "their", "theirs",
        "them", "themselves", "then", "there", "there's", "these", "they", "they'd", "they'll", "they're",
        "they've", "this", "those", "through", "to", "too", "under", "until", "up", "very", "was", "wasn't",
        "we", "we'd", "we'll", "we're", "we've", "were", "weren't", "what", "what's", "when", "when's",
        "where", "where's", "which", "while", "who", "who's", "whom", "why", "why's", "with", "won't",
        "would", "wouldn't", "you", "you'd", "you'll", "you're", "you've", "your", "yours", "yourself", "yourselves"
    };

    public static List<string> Tokenize(string text)
    {
        var matches = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z0-9]{2,}\b");
        var tokens = new List<string>();
        foreach (Match m in matches)
        {
            if (!StopWords.Contains(m.Value))
            {
                tokens.Add(m.Value);
            }
        }
        return tokens;
    }

    public static List<(SourceChunkItem Chunk, double Score)> RankPassages(string query, List<SourceChunkItem> passages, int limit = 5)
    {
        if (passages.Count == 0) return new();

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return passages.OrderBy(p => p.ChunkIndex).Take(limit).Select(p => (p, 1.0)).ToList();
        }

        // Compute TF-IDF across passages
        int nDocs = passages.Count;
        var df = new Dictionary<string, int>();
        var docTfs = new List<Dictionary<string, double>>();

        foreach (var p in passages)
        {
            var tokens = Tokenize(p.Content);
            var tf = new Dictionary<string, double>();
            var seen = new HashSet<string>();
            foreach (var t in tokens)
            {
                tf[t] = tf.GetValueOrDefault(t, 0) + 1;
                if (seen.Add(t))
                {
                    df[t] = df.GetValueOrDefault(t, 0) + 1;
                }
            }

            // sublinear tf: 1 + log(tf)
            var normTf = new Dictionary<string, double>();
            foreach (var kvp in tf)
            {
                normTf[kvp.Key] = 1.0 + Math.Log(kvp.Value);
            }
            docTfs.Add(normTf);
        }

        // Query TF
        var queryTf = new Dictionary<string, double>();
        foreach (var t in queryTokens)
        {
            queryTf[t] = queryTf.GetValueOrDefault(t, 0) + 1;
        }

        // Compute cosine similarity for each document
        var scores = new List<(SourceChunkItem Chunk, double Score)>();
        for (int i = 0; i < passages.Count; i++)
        {
            var pTf = docTfs[i];
            double dot = 0.0;
            double normQ = 0.0;
            double normDoc = 0.0;

            foreach (var (qTerm, count) in queryTf)
            {
                var idf = Math.Log((nDocs + 1.0) / (df.GetValueOrDefault(qTerm, 0) + 1.0)) + 1.0;
                var qWeight = (1.0 + Math.Log(count)) * idf;
                normQ += qWeight * qWeight;

                if (pTf.TryGetValue(qTerm, out var pCount))
                {
                    var docWeight = pCount * idf;
                    dot += qWeight * docWeight;
                }
            }

            foreach (var (docTerm, pCount) in pTf)
            {
                var idf = Math.Log((nDocs + 1.0) / (df.GetValueOrDefault(docTerm, 0) + 1.0)) + 1.0;
                var docWeight = pCount * idf;
                normDoc += docWeight * docWeight;
            }

            double sim = 0.0;
            if (normQ > 0 && normDoc > 0)
            {
                sim = dot / (Math.Sqrt(normQ) * Math.Sqrt(normDoc));
            }

            scores.Add((passages[i], sim));
        }

        var ranked = scores.Where(s => s.Score > 0.001).OrderByDescending(s => s.Score).Take(limit).ToList();
        if (ranked.Count == 0)
        {
            return passages.OrderBy(p => p.ChunkIndex).Take(limit).Select(p => (p, 0.5)).ToList();
        }

        return ranked;
    }

    public static List<SourceChunkItem> SelectRepresentativeChunks(List<SourceChunkItem> chunks, int maxCount = 15)
    {
        if (chunks.Count <= maxCount) return chunks;

        var selected = new List<SourceChunkItem>();
        selected.AddRange(chunks.Take(2));
        double step = (double)(chunks.Count - 2) / (maxCount - 3);

        for (int i = 0; i < maxCount - 3; i++)
        {
            int idx = (int)(2 + i * step);
            if (idx < chunks.Count - 1 && !selected.Contains(chunks[idx]))
            {
                selected.Add(chunks[idx]);
            }
        }

        if (!selected.Contains(chunks[^1]))
        {
            selected.Add(chunks[^1]);
        }

        return selected.OrderBy(c => c.ChunkIndex).ToList();
    }
}

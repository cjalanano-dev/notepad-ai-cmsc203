using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class ChatService
{
    private readonly OllamaService _ollama;

    public ChatService(OllamaService ollama)
    {
        _ollama = ollama;
    }

    public async Task<ChatMessage> AskReviewerAsync(
        string docFilename,
        List<SourceChunkItem> allPassages,
        string question,
        List<ChatMessage>? history = null,
        string? model = null,
        CancellationToken ct = default)
    {
        question = question.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Enter a question first.");
        }

        if (allPassages.Count == 0)
        {
            throw new InvalidOperationException("This reviewer has no extracted passages yet.");
        }

        // Rank passages via TF-IDF vector similarity
        var ranked = VectorService.RankPassages(question, allPassages, limit: 5);
        var selectedPassages = ranked.Select(r => r.Chunk).ToList();

        var sources = string.Join("\n\n", selectedPassages.Select(p => $"[Passage {p.Id}, page {p.PageNumber}]\n{p.Content}"));

        var systemPrompt =
            "Answer the learner's question using only the supplied reviewer passages. " +
            "Use earlier conversation only to understand follow-up references, never as evidence. " +
            "If the passages do not contain the answer, say so clearly. Do not add outside facts. " +
            "Cite supporting passages inline using [Passage ID, p. page].";

        var historySnippet = "";
        if (history != null && history.Count > 0)
        {
            var recent = history.TakeLast(6);
            historySnippet = "Recent conversation:\n" + string.Join("\n", recent.Select(m => $"{m.Role}: {m.Content}")) + "\n\n";
        }

        var userPrompt = $"{historySnippet}Reviewer: {docFilename}\n\nQuestion: {question}\n\nPassages:\n{sources}";

        var answer = await _ollama.GenerateTextAsync(systemPrompt, userPrompt, model, ct);

        var citations = selectedPassages.Select(p => new ReviewerCitation
        {
            PassageId = p.Id,
            PageNumber = p.PageNumber,
            Excerpt = p.Content.Length > 280 ? p.Content.Substring(0, 280).Trim() : p.Content.Trim()
        }).ToList();

        return new ChatMessage
        {
            Role = "assistant",
            Content = answer,
            Citations = citations,
            Timestamp = DateTime.Now
        };
    }
}

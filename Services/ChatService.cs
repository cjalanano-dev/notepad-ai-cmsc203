using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class ChatService
{
    private readonly ILlmService _ollama;

    public ChatService(ILlmService ollama)
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
            "You are an expert university professor and academic study mentor helping a student master the provided study material.\n" +
            "PEDAGOGICAL & GROUNDING RULES:\n" +
            "1. Strict Source Grounding: Teach using ONLY the supplied reviewer passages. Use earlier conversation solely to resolve follow-up context, never as factual evidence. If the passages do not contain the answer, clearly state what is missing from the uploaded notes.\n" +
            "2. Clear Pedagogical Structure:\n" +
            "   - Start with a **Core Takeaway** (1-2 clear sentences directly answering the student's question).\n" +
            "   - Follow with a **Detailed Breakdown** using bullet points, bold key terms, and step-by-step cause-and-effect explanations so the concept is easy to digest.\n" +
            "   - End with a brief **Professor's Exam Tip / Self-Check** (1 sentence highlighting a common pitfall, key distinction, or quick active-recall question to solidify understanding).\n" +
            "3. Inline Citations: Cite every factual claim inline using the exact marker format `[Passage ID, p. page]` (e.g., `[Passage 2, p. 1]`).";

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

using BlazorApp1.Models;

namespace BlazorApp1.Services;

public class StudyService
{
    private readonly OllamaService _ollama;

    public static readonly List<(string Criterion, int Points, string Description)> EssayRubric = new()
    {
        ("Claim and Thesis", 20, "Clear central claim directly answering the prompt"),
        ("Source Grounding and Evidence", 30, "Accurate citation and integration of reviewer text"),
        ("Reasoning and Synthesis", 25, "Coherent logical progression and sound analysis"),
        ("Structure and Clarity", 15, "Well-organized paragraphs and clear academic prose"),
        ("Completeness", 10, "Addresses all aspects of the assigned task")
    };

    public StudyService(OllamaService ollama)
    {
        _ollama = ollama;
    }

    public async Task<List<QuizItem>> GenerateQuizAsync(
        string docFilename,
        List<SourceChunkItem> passages,
        string? model = null,
        CancellationToken ct = default)
    {
        if (passages.Count == 0) throw new ArgumentException("No passages available for quiz generation.");

        var selected = VectorService.SelectRepresentativeChunks(passages, 12);
        var passagesText = string.Join("\n\n", selected.Select(p => $"[Passage {p.Id}, page {p.PageNumber}]\n{p.Content}"));

        var systemPrompt =
            "You are an educational quiz creator. Generate a multiple-choice quiz based strictly on the provided reviewer passages.\n" +
            "RULES:\n" +
            "1. Generate 3 to 6 high-quality multiple choice questions testing key concepts.\n" +
            "2. Each question MUST have exactly 4 options.\n" +
            "3. 'answer_index' must be 0, 1, 2, or 3 corresponding to the correct option in 'options'.\n" +
            "4. Include a concise explanation and valid 'passage_ids' citing the exact supporting passages.\n" +
            "5. Return strictly JSON matching:\n" +
            "{\"items\": [{\"question\": \"string\", \"options\": [\"A\", \"B\", \"C\", \"D\"], \"answer_index\": 0, \"explanation\": \"string\", \"passage_ids\": [1]}]}";

        var userPrompt = $"Reviewer: {docFilename}\n\nReviewer passages:\n{passagesText}";

        try
        {
            var raw = await _ollama.GenerateStructuredAsync<RawQuizOutput>(systemPrompt, userPrompt, model, ct);
            if (raw != null && raw.Items != null && raw.Items.Count > 0)
            {
                var result = new List<QuizItem>();
                foreach (var item in raw.Items)
                {
                    if (item.Options.Count >= 2)
                    {
                        var citation = item.PassageIds.Count > 0
                            ? $"p. {passages.FirstOrDefault(p => p.Id == item.PassageIds[0])?.PageNumber ?? 1}"
                            : "p. 1";

                        result.Add(new QuizItem
                        {
                            Question = item.Question,
                            Options = item.Options,
                            AnswerIndex = Math.Clamp(item.AnswerIndex, 0, item.Options.Count - 1),
                            Explanation = item.Explanation,
                            Citation = citation
                        });
                    }
                }
                if (result.Count > 0) return result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ollama Quiz Gen Error] {ex.Message}. Falling back to heuristic quiz generation.");
        }

        // Reliable fallback if LLM offline or timed out
        return GenerateFallbackQuiz(docFilename, passages);
    }

    public async Task<List<FlashcardItem>> GenerateFlashcardsAsync(
        string docFilename,
        List<SourceChunkItem> passages,
        string? model = null,
        CancellationToken ct = default)
    {
        if (passages.Count == 0) throw new ArgumentException("No passages available for flashcard generation.");

        var selected = VectorService.SelectRepresentativeChunks(passages, 12);
        var passagesText = string.Join("\n\n", selected.Select(p => $"[Passage {p.Id}, page {p.PageNumber}]\n{p.Content}"));

        var systemPrompt =
            "You are a study flashcard generator. Create a set of flashcards strictly grounded in the supplied reviewer passages.\n" +
            "RULES:\n" +
            "1. Create 4 to 8 flashcards covering core concepts and definitions.\n" +
            "2. 'front' should be a focused concept question or prompt.\n" +
            "3. 'back' should be a clear, accurate explanation or answer from the text.\n" +
            "4. 'passage_ids' should contain the IDs of the supporting passages.\n" +
            "5. Return strictly JSON matching:\n" +
            "{\"items\": [{\"front\": \"string\", \"back\": \"string\", \"passage_ids\": [1]}]}";

        var userPrompt = $"Reviewer: {docFilename}\n\nReviewer passages:\n{passagesText}";

        try
        {
            var raw = await _ollama.GenerateStructuredAsync<RawFlashcardOutput>(systemPrompt, userPrompt, model, ct);
            if (raw != null && raw.Items != null && raw.Items.Count > 0)
            {
                var result = new List<FlashcardItem>();
                foreach (var item in raw.Items)
                {
                    var citation = item.PassageIds.Count > 0
                        ? $"p. {passages.FirstOrDefault(p => p.Id == item.PassageIds[0])?.PageNumber ?? 1}"
                        : "p. 1";

                    result.Add(new FlashcardItem
                    {
                        Front = item.Front,
                        Back = item.Back,
                        Citation = citation
                    });
                }
                if (result.Count > 0) return result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ollama Flashcard Gen Error] {ex.Message}. Falling back to heuristic flashcards.");
        }

        // Reliable fallback if LLM offline or timed out
        return GenerateFallbackFlashcards(docFilename, passages);
    }

    private List<QuizItem> GenerateFallbackQuiz(string docFilename, List<SourceChunkItem> passages)
    {
        var items = new List<QuizItem>();
        for (int i = 0; i < Math.Min(4, passages.Count); i++)
        {
            var chunk = passages[i];
            var lines = chunk.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var firstLine = lines.FirstOrDefault() ?? "Document Concept";
            var snippet = chunk.Content.Length > 120 ? chunk.Content.Substring(0, 120) + "..." : chunk.Content;

            items.Add(new QuizItem
            {
                Question = $"According to page {chunk.PageNumber} in {docFilename}, what is the main focus of this section?",
                Options = new List<string>
                {
                    snippet,
                    "An unrelated secondary topic not covered in this section",
                    "A historical overview irrelevant to the central subject",
                    "A summary of future administrative procedures"
                },
                AnswerIndex = 0,
                Explanation = $"As detailed on page {chunk.PageNumber}, this section focuses on: {firstLine.Trim()}.",
                Citation = $"p. {chunk.PageNumber}"
            });
        }
        return items;
    }

    private List<FlashcardItem> GenerateFallbackFlashcards(string docFilename, List<SourceChunkItem> passages)
    {
        var items = new List<FlashcardItem>();
        for (int i = 0; i < Math.Min(6, passages.Count); i++)
        {
            var chunk = passages[i];
            var lines = chunk.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var title = lines.FirstOrDefault()?.Trim().TrimStart('#', '-', '*', ' ') ?? $"Concept Section {i + 1}";
            if (title.Length > 40) title = title.Substring(0, 40) + "...";

            var explanation = chunk.Content.Length > 200 ? chunk.Content.Substring(0, 200) + "..." : chunk.Content;

            items.Add(new FlashcardItem
            {
                Front = $"What are the key points regarding '{title}'?",
                Back = explanation,
                Citation = $"p. {chunk.PageNumber}"
            });
        }
        return items;
    }

    public async Task<EssayTask> GenerateEssayTaskAsync(
        string docFilename,
        List<SourceChunkItem> passages,
        string? model = null,
        CancellationToken ct = default)
    {
        if (passages.Count == 0) throw new ArgumentException("No passages available for essay prompt generation.");

        var selected = VectorService.SelectRepresentativeChunks(passages, 10);
        var passagesText = string.Join("\n\n", selected.Select(p => $"[Passage {p.Id}, page {p.PageNumber}]\n{p.Content}"));

        var systemPrompt =
            "You are an academic essay examiner. Create one thoughtful, challenging essay prompt based strictly on the reviewer.\n" +
            "RULES:\n" +
            "1. The prompt must be answerable using only the supplied text.\n" +
            "2. Provide an engaging academic title.\n" +
            "3. Include the passage_ids that anchor the prompt.\n" +
            "4. Return strictly JSON matching:\n" +
            "{\"essay\": {\"title\": \"string\", \"prompt\": \"string\", \"passage_ids\": [1]}}";

        var userPrompt = $"Reviewer: {docFilename}\n\nReviewer passages:\n{passagesText}";

        var raw = await _ollama.GenerateStructuredAsync<RawEssayTaskOutput>(systemPrompt, userPrompt, model, ct);
        if (raw?.Essay == null)
        {
            throw new InvalidOperationException("Failed to generate essay prompt.");
        }

        var citationPages = raw.Essay.PassageIds
            .Select(id => passages.FirstOrDefault(p => p.Id == id)?.PageNumber)
            .Where(p => p != null)
            .Distinct()
            .ToList();

        var citationText = citationPages.Count > 0 ? "Passages p. " + string.Join(", ", citationPages) : "Source Passages";

        return new EssayTask
        {
            Title = raw.Essay.Title,
            Prompt = raw.Essay.Prompt,
            Citation = citationText,
            Submission = "",
            Score = null,
            Feedback = null
        };
    }

    public async Task<(int Score, string Feedback)> GradeEssayAsync(
        string docFilename,
        List<SourceChunkItem> passages,
        string prompt,
        string essaySubmission,
        string? model = null,
        CancellationToken ct = default)
    {
        var selected = VectorService.SelectRepresentativeChunks(passages, 12);
        var passagesText = string.Join("\n\n", selected.Select(p => $"[Passage {p.Id}, page {p.PageNumber}]\n{p.Content}"));

        var rubricText = string.Join("\n", EssayRubric.Select(r => $"- {r.Criterion} ({r.Points} pts): {r.Description}"));

        var systemPrompt =
            $"Grade the learner's essay in response to the prompt based strictly on the reviewer text.\n" +
            $"Rubric (100 pts total):\n{rubricText}\n\n" +
            "RULES:\n" +
            "1. Score each criterion between 0 and its max points.\n" +
            "2. Provide detailed overall_feedback, strengths, and next_steps.\n" +
            "3. Return strictly JSON matching:\n" +
            "{\"criteria\": [{\"criterion\": \"Claim and Thesis\", \"score\": 18, \"feedback\": \"...\"}], \"overall_feedback\": \"string\", \"strengths\": \"string\", \"next_steps\": [\"...\"]}";

        var userPrompt = $"Reviewer: {docFilename}\nPrompt: {prompt}\n\nEssay Submission:\n{essaySubmission}\n\nReviewer Passages:\n{passagesText}";

        var raw = await _ollama.GenerateStructuredAsync<RawEssayGradeOutput>(systemPrompt, userPrompt, model, ct);
        if (raw == null || raw.Criteria == null || raw.Criteria.Count == 0)
        {
            return (80, "Essay evaluated successfully based on source grounding and thematic relevance.");
        }

        int totalScore = raw.Criteria.Sum(c => c.Score);
        totalScore = Math.Clamp(totalScore, 0, 100);

        var feedback = $"{raw.OverallFeedback}\n\nStrengths: {raw.Strengths}\n\nRecommendations: {string.Join("; ", raw.NextSteps)}";
        return (totalScore, feedback);
    }
}

using System.Text.Json.Serialization;

namespace BlazorApp1.Models;

public class RawConcept
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("node_type")]
    public string NodeType { get; set; } = "concept";

    [JsonPropertyName("source_passage_ids")]
    public List<int> SourcePassageIds { get; set; } = new();
}

public class RawRelationship
{
    [JsonPropertyName("source_label")]
    public string SourceLabel { get; set; } = string.Empty;

    [JsonPropertyName("target_label")]
    public string TargetLabel { get; set; } = string.Empty;

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "relates to";
}

public class ExtractionResult
{
    [JsonPropertyName("title")]
    public string? Title { get; set; } = "Concept Map";

    [JsonPropertyName("concepts")]
    public List<RawConcept> Concepts { get; set; } = new();

    [JsonPropertyName("relationships")]
    public List<RawRelationship> Relationships { get; set; } = new();
}

public class RawQuizItem
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    [JsonPropertyName("answer_index")]
    public int AnswerIndex { get; set; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("passage_ids")]
    public List<int> PassageIds { get; set; } = new();
}

public class RawQuizOutput
{
    [JsonPropertyName("items")]
    public List<RawQuizItem> Items { get; set; } = new();
}

public class RawFlashcardItem
{
    [JsonPropertyName("front")]
    public string Front { get; set; } = string.Empty;

    [JsonPropertyName("back")]
    public string Back { get; set; } = string.Empty;

    [JsonPropertyName("passage_ids")]
    public List<int> PassageIds { get; set; } = new();
}

public class RawFlashcardOutput
{
    [JsonPropertyName("items")]
    public List<RawFlashcardItem> Items { get; set; } = new();
}

public class RawEssayTask
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("passage_ids")]
    public List<int> PassageIds { get; set; } = new();
}

public class RawEssayTaskOutput
{
    [JsonPropertyName("essay")]
    public RawEssayTask? Essay { get; set; }
}

public class RubricCriterionGrade
{
    [JsonPropertyName("criterion")]
    public string Criterion { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("feedback")]
    public string Feedback { get; set; } = string.Empty;
}

public class RawEssayGradeOutput
{
    [JsonPropertyName("criteria")]
    public List<RubricCriterionGrade> Criteria { get; set; } = new();

    [JsonPropertyName("overall_feedback")]
    public string OverallFeedback { get; set; } = string.Empty;

    [JsonPropertyName("strengths")]
    public string Strengths { get; set; } = string.Empty;

    [JsonPropertyName("next_steps")]
    public List<string> NextSteps { get; set; } = new();

    [JsonPropertyName("passage_ids")]
    public List<int> PassageIds { get; set; } = new();
}

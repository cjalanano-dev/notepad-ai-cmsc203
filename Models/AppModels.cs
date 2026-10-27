namespace BlazorApp1.Models;

public class UserProfile
{
    public string Id { get; set; } = "user_1";
    public string? Email { get; set; } = "carlos@notepad.ai";
    public string? Role { get; set; } = "Learner";
    public string? DisplayName { get; set; } = "Carlos James";
}

public class DocumentItem
{
    public int Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public string ProcessingStatus { get; set; } = "ready"; // ready, processing, completed, error
    public int ChunkCount { get; set; } = 12;
    public bool HasMap { get; set; } = true;
    public int? ProjectId { get; set; }
    public string? CoverUrl { get; set; }
    public List<SourceChunkItem> Passages { get; set; } = new();
}

public class ProjectFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int DocumentCount { get; set; }
}

public class ConceptNode
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string NodeType { get; set; } = "concept"; // root, concept, subconcept
    public double X { get; set; }
    public double Y { get; set; }
    public List<int> SourceChunkIds { get; set; } = new();
}

public class ConceptEdge
{
    public int Id { get; set; }
    public int SourceNodeId { get; set; }
    public int TargetNodeId { get; set; }
    public string Relationship { get; set; } = string.Empty;
}

public class ConceptDetail
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool HasGeneratedExplanation { get; set; }
    public string NodeType { get; set; } = "concept";
    public List<SourceChunkItem> Sources { get; set; } = new();
}

public class SourceChunkItem
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? DocumentFilename { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; } = "assistant"; // user, assistant
    public string Content { get; set; } = string.Empty;
    public List<ReviewerCitation> Citations { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class ReviewerCitation
{
    public int PassageId { get; set; }
    public int PageNumber { get; set; }
    public string Excerpt { get; set; } = string.Empty;
}

public class QuizItem
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int AnswerIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Citation { get; set; } = "p. 14";
}

public class FlashcardItem
{
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public string Citation { get; set; } = "p. 22";
}

public class EssayTask
{
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Citation { get; set; } = "p. 5 - 12";
    public string Submission { get; set; } = string.Empty;
    public int? Score { get; set; }
    public string? Feedback { get; set; }
}

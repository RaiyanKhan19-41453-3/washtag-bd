using System.ComponentModel.DataAnnotations;

namespace Washtag.Application.DTOs;

public class AssistantAskRequest
{
    [Required, MaxLength(500)] public string Question { get; set; } = string.Empty;
}

public class AssistantSourceDto
{
    public string Type { get; set; } = string.Empty;
    public Guid RefId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public double Similarity { get; set; }
}

public class AssistantAnswerDto
{
    public string Answer { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public bool AiPhrased { get; set; }
    public string Provider { get; set; } = string.Empty;
    public List<AssistantSourceDto> Sources { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}

public class AssistantStatusDto
{
    public bool VectorReady { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DocumentRef
{
    public string DocType { get; set; } = string.Empty;
    public Guid RefId { get; set; }
}

public class DocumentMatch
{
    public string DocType { get; set; } = string.Empty;
    public Guid RefId { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Similarity { get; set; }
}

using Washtag.Application.DTOs;
using Washtag.Domain.Entities;

namespace Washtag.Application.Interfaces;

public static class AiConstants
{
    public const int EmbeddingDimensions = 384;
}

/// <summary>Embedding + optional LLM phrasing. Local hashing is used when no API key is configured.</summary>
public interface IAiProvider
{
    string Name { get; }
    string EmbeddingModel { get; }
    bool UsedLocalEmbedding { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    Task<string?> GenerateAnswerAsync(string question, string groundedContext, CancellationToken cancellationToken = default);
}

public interface IVectorStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(Guid shopId, string docType, Guid refId, string content, float[] embedding, CancellationToken cancellationToken = default);
    Task<List<DocumentMatch>> SearchAsync(Guid shopId, float[] embedding, int limit, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<List<DocumentRef>> GetRefsAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid shopId, string docType, Guid refId, CancellationToken cancellationToken = default);
}

public interface IDocumentIndexer
{
    Task IndexOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task IndexCustomerAsync(Guid shopId, string phone, CancellationToken cancellationToken = default);
    Task ReindexShopAsync(Guid shopId, CancellationToken cancellationToken = default);
}

public interface IAssistantService
{
    Task<AssistantAnswerDto> AskAsync(Guid shopId, Guid userId, string question, CancellationToken cancellationToken = default);
    Task<List<AssistantAnswerDto>> GetHistoryAsync(Guid shopId, Guid userId, int limit, CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(Guid shopId, Guid userId, CancellationToken cancellationToken = default);
    Task SubmitFeedbackAsync(Guid shopId, Guid userId, AssistantFeedbackRequest request, CancellationToken cancellationToken = default);
    Task<AssistantStatusDto> GetStatusAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<int> ReindexAsync(Guid shopId, CancellationToken cancellationToken = default);
}

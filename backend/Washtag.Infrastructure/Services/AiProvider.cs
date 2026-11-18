using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

/// <summary>
/// Uses an OpenAI-compatible embeddings/chat endpoint when OPENAI_API_KEY is set.
/// Otherwise falls back to deterministic hashed embeddings and grounded local answers,
/// so the assistant works fully offline with no keys.
/// </summary>
public class AiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _embeddingModel;
    private readonly string _chatModel;
    private readonly bool _usingGateway;

    public AiProvider(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        var gatewayToken = configuration["AI_GATEWAY_TOKEN"];
        var gatewayUrl = configuration["AI_GATEWAY_URL"];
        _usingGateway = string.IsNullOrWhiteSpace(configuration["OPENAI_API_KEY"]) && !string.IsNullOrWhiteSpace(gatewayToken);
        _apiKey = configuration["OPENAI_API_KEY"] ?? gatewayToken ?? string.Empty;
        _baseUrl = (configuration["OPENAI_BASE_URL"] ?? gatewayUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        _embeddingModel = configuration["OPENAI_EMBEDDING_MODEL"] ?? configuration["AI_GATEWAY_EMBEDDING_MODEL"] ?? "text-embedding-3-small";
        _chatModel = configuration["OPENAI_CHAT_MODEL"] ?? configuration["AI_GATEWAY_CHAT_MODEL"] ?? "gpt-4o-mini";
    }

    public string Name => string.IsNullOrWhiteSpace(_apiKey)
        ? "Local offline assistant"
        : _usingGateway ? "AI Gateway" : "OpenAI-compatible";
    public string EmbeddingModel => string.IsNullOrWhiteSpace(_apiKey) ? "hash-384 (offline)" : _embeddingModel;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return LocalEmbed(text);

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                input = text,
                model = _embeddingModel,
                dimensions = AiConstants.EmbeddingDimensions
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/embeddings")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var values = document.RootElement.GetProperty("data")[0].GetProperty("embedding")
                .EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();

            return Resize(values, AiConstants.EmbeddingDimensions);
        }
        catch
        {
            return LocalEmbed(text);
        }
    }

    public async Task<string?> GenerateAnswerAsync(string question, string groundedContext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return null;

        try
        {
            var system = "You are the shop assistant for WashTag BD, a laundry SaaS in Bangladesh. " +
                         "Answer using ONLY the facts and retrieved records provided. Be concise and concrete. " +
                         "Use BDT amounts with the ৳ symbol. Never invent numbers. " +
                         "If the user writes in Banglish, you may reply in friendly Banglish; otherwise use simple English.";
            var payload = JsonSerializer.Serialize(new
            {
                model = _chatModel,
                temperature = 0.2,
                max_tokens = 350,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = $"Question: {question}\n\n{groundedContext}" }
                }
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static float[] Resize(float[] values, int dimensions)
    {
        if (values.Length == dimensions) return values;
        var result = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
            result[i] = i < values.Length ? values[i] : 0f;
        return result;
    }

    private static float[] LocalEmbed(string text)
    {
        var vector = new float[AiConstants.EmbeddingDimensions];
        var tokens = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value).ToList();

        void Add(string token)
        {
            var hash = Fnv1a(token);
            var index = (int)((uint)hash % AiConstants.EmbeddingDimensions);
            vector[index] += (hash & 1) == 0 ? 1f : -1f;
        }

        foreach (var token in tokens) Add(token);
        for (var i = 0; i < tokens.Count - 1; i++) Add(tokens[i] + "_" + tokens[i + 1]);

        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++) vector[i] /= norm;

        return vector;
    }

    private static int Fnv1a(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}

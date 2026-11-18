using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;
using Washtag.Domain.Entities;
using Washtag.Domain.Enums;
using Washtag.Domain.Exceptions;

namespace Washtag.Application.Services;

public class AssistantService : IAssistantService
{
    private readonly IAppDbContext _db;
    private readonly IVectorStore _vectors;
    private readonly IAiProvider _ai;
    private readonly IDocumentIndexer _indexer;

    private static readonly List<string> Suggestions = new()
    {
        "Show me customers who haven't picked up their clothes in 5 days",
        "What is the total unpaid bill for Mr. Rahman?",
        "How much money have I collected this month?",
        "How many orders are currently in progress?"
    };

    public AssistantService(IAppDbContext db, IVectorStore vectors, IAiProvider ai, IDocumentIndexer indexer)
    {
        _db = db;
        _vectors = vectors;
        _ai = ai;
        _indexer = indexer;
    }

    public async Task<AssistantAnswerDto> AskAsync(Guid shopId, Guid userId, string question, CancellationToken cancellationToken = default)
    {
        var query = question.Trim();
        var plan = ParseIntent(query);

        var matches = new List<DocumentMatch>();
        try
        {
            var embedding = await _ai.EmbedAsync(query, cancellationToken);
            matches = await _vectors.SearchAsync(shopId, embedding, 8, cancellationToken);
        }
        catch { /* Vector search is an aid; deterministic facts below always work. */ }

        var facts = await BuildFactsAsync(shopId, plan, cancellationToken);
        var context = BuildContext(facts, matches);

        string? phrased = null;
        try { phrased = await _ai.GenerateAnswerAsync(query, context, cancellationToken); }
        catch { /* Fall back to the grounded local answer. */ }

        var answer = string.IsNullOrWhiteSpace(phrased)
            ? ComposeLocalAnswer(facts, matches, plan)
            : phrased.Trim();

        var sources = BuildSources(matches);
        var result = new AssistantAnswerDto
        {
            Answer = answer,
            Intent = plan.Intent,
            AiPhrased = !string.IsNullOrWhiteSpace(phrased),
            Provider = _ai.Name,
            Sources = sources,
            Suggestions = Suggestions,
            AnsweredAt = DateTime.UtcNow
        };

        var record = new AssistantMessage
        {
            ShopId = shopId,
            UserId = userId,
            Role = "assistant",
            Text = answer,
            SourcesJson = sources.Count > 0 ? JsonSerializer.Serialize(sources) : null
        };
        var savedAt = DateTime.UtcNow;
        _db.AssistantMessages.Add(new AssistantMessage
        {
            ShopId = shopId, UserId = userId, Role = "user", Text = query, CreatedAt = savedAt
        });
        record.CreatedAt = savedAt.AddMilliseconds(1);
        _db.AssistantMessages.Add(record);
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var stale = await _db.AssistantMessages
            .Where(m => m.ShopId == shopId && m.CreatedAt < cutoff).ToListAsync(cancellationToken);
        _db.AssistantMessages.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        result.Id = record.Id;
        result.AnsweredAt = record.CreatedAt;
        return result;
    }

    public async Task<List<AssistantAnswerDto>> GetHistoryAsync(Guid shopId, Guid userId, int limit,
        CancellationToken cancellationToken = default)
    {
        var messages = await _db.AssistantMessages.AsNoTracking()
            .Where(m => m.ShopId == shopId && m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200) * 2)
            .ToListAsync(cancellationToken);

        return messages.Select(m => new AssistantAnswerDto
        {
            Id = m.Role == "assistant" ? m.Id : Guid.Empty,
            Role = m.Role,
            Answer = m.Text,
            Text = m.Text,
            Rating = m.Rating,
            Sources = string.IsNullOrEmpty(m.SourcesJson)
                ? new List<AssistantSourceDto>()
                : JsonSerializer.Deserialize<List<AssistantSourceDto>>(m.SourcesJson) ?? new List<AssistantSourceDto>(),
            AnsweredAt = m.CreatedAt
        }).OrderBy(m => m.AnsweredAt).ToList();
    }

    public async Task ClearHistoryAsync(Guid shopId, Guid userId, CancellationToken cancellationToken = default)
    {
        var messages = await _db.AssistantMessages
            .Where(m => m.ShopId == shopId && m.UserId == userId).ToListAsync(cancellationToken);
        _db.AssistantMessages.RemoveRange(messages);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitFeedbackAsync(Guid shopId, Guid userId, AssistantFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var message = await _db.AssistantMessages.FirstOrDefaultAsync(
            m => m.Id == request.MessageId && m.ShopId == shopId && m.UserId == userId &&
                 m.Role == "assistant", cancellationToken)
            ?? throw new AppException("Message not found.");
        message.Rating = Math.Clamp(request.Rating, -1, 1);
        message.FeedbackComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AssistantStatusDto> GetStatusAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        int count = 0;
        var ready = true;
        var message = "Vector search is active.";
        try { count = await _vectors.CountAsync(shopId, cancellationToken); }
        catch { ready = false; message = "Vector search is temporarily unavailable."; }

        return new AssistantStatusDto
        {
            VectorReady = ready,
            Provider = _ai.Name,
            EmbeddingModel = _ai.EmbeddingModel,
            DocumentCount = count,
            Message = message
        };
    }

    public async Task<int> ReindexAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        await _indexer.ReindexShopAsync(shopId, cancellationToken);
        return await _vectors.CountAsync(shopId, cancellationToken);
    }

    private async Task<FactSet> BuildFactsAsync(Guid shopId, QueryPlan plan, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        switch (plan.Intent)
        {
            case "unpaid":
            {
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId && o.Status != OrderStatus.Cancelled &&
                                o.PaymentStatus != PaymentStatus.Paid)
                    .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
                orders = Filter(orders, plan.CustomerName);
                var amount = orders.Sum(o => o.TotalAmount - o.PaidAmount);
                var names = string.Join(", ", orders.Take(8).Select(o => $"{o.CustomerName} (#{o.OrderNumber}, ৳{o.TotalAmount - o.PaidAmount:0})"));
                var text = orders.Count == 0
                    ? (string.IsNullOrEmpty(plan.CustomerName)
                        ? "There are no unpaid balances in the records."
                        : $"No unpaid orders found for {plan.CustomerName}.")
                    : $"Total unpaid balance is ৳{amount:0} across {orders.Count} order(s)" +
                      (string.IsNullOrEmpty(plan.CustomerName) ? ": " : $" for {plan.CustomerName}: ") + names + ".";
                return new FactSet(text, orders);
            }

            case "pickup":
            {
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId && o.Status == OrderStatus.ReadyForPickup)
                    .ToListAsync(ct);
                orders = Filter(orders, plan.CustomerName);
                var orderIds = orders.Select(o => o.Id).ToList();
                var readyEvents = await _db.StatusEvents.AsNoTracking()
                    .Where(e => orderIds.Contains(e.OrderId) && e.ToStatus == OrderStatus.ReadyForPickup)
                    .ToListAsync(ct);
                DateTime ReadyAt(Order o) => readyEvents
                    .Where(e => e.OrderId == o.Id).Select(e => e.CreatedAt)
                    .DefaultIfEmpty(o.UpdatedAt).Max();

                var cutoffDays = plan.Days ?? 0;
                var waiting = cutoffDays > 0
                    ? orders.Where(o => ReadyAt(o) <= now.AddDays(-cutoffDays)).ToList()
                    : orders;
                waiting = waiting.OrderBy(ReadyAt).ToList();
                var text = waiting.Count == 0
                    ? $"No orders have been waiting for pickup{(cutoffDays > 0 ? $" longer than {cutoffDays} day(s)" : "")}."
                    : $"{waiting.Count} order(s) have been ready for pickup{(cutoffDays > 0 ? $" for more than {cutoffDays} day(s)" : "")}: " +
                      string.Join(", ", waiting.Take(10).Select(o =>
                          $"{o.CustomerName} (#{o.OrderNumber}, waiting since {ReadyAt(o):dd MMM})")) + ".";
                return new FactSet(text, waiting);
            }

            case "revenue":
            {
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId && o.Status != OrderStatus.Cancelled).ToListAsync(ct);
                if (plan.Days.HasValue)
                    orders = orders.Where(o => o.CreatedAt >= now.AddDays(-plan.Days.Value)).ToList();
                else if (plan.MonthOnly)
                {
                    var local = now.AddHours(6);
                    var monthStart = new DateTime(local.Year, local.Month, 1).AddHours(-6);
                    orders = orders.Where(o => o.CreatedAt >= monthStart).ToList();
                }
                orders = Filter(orders, plan.CustomerName);
                var collected = orders.Sum(o => o.PaidAmount);
                var outstanding = orders.Sum(o => o.TotalAmount - o.PaidAmount);
                var period = plan.Days.HasValue ? $"in the last {plan.Days.Value} day(s)"
                    : plan.MonthOnly ? "this month" : "in total";
                return new FactSet(
                    $"Amount collected {period} is ৳{collected:0} across {orders.Count} order(s). Outstanding balance: ৳{outstanding:0}.",
                    orders);
            }

            case "count":
            case "active":
            {
                var orders = await _db.Orders.AsNoTracking().Where(o => o.ShopId == shopId).ToListAsync(ct);
                var active = orders.Count(o => o.Status is OrderStatus.Received or OrderStatus.Washing
                    or OrderStatus.Ironing or OrderStatus.ReadyForPickup);
                var ready = orders.Count(o => o.Status == OrderStatus.ReadyForPickup);
                var delivered = orders.Count(o => o.Status == OrderStatus.Delivered);
                var text = plan.Intent == "active"
                    ? $"There are {active} active order(s), including {ready} ready for pickup. Delivered so far: {delivered}."
                    : $"The shop has {orders.Count} total order(s): {active} active and {delivered} delivered.";
                return new FactSet(text, orders.Where(o => o.Status != OrderStatus.Delivered).ToList());
            }

            case "customer":
            {
                var customers = await _db.Customers.AsNoTracking().Where(c => c.ShopId == shopId).ToListAsync(ct);
                if (!string.IsNullOrEmpty(plan.CustomerName))
                    customers = customers.Where(c => c.Name.ToLower().Contains(plan.CustomerName.ToLower())).ToList();
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId && o.Status != OrderStatus.Cancelled).ToListAsync(ct);
                orders = Filter(orders, plan.CustomerName);
                var text = customers.Count == 0
                    ? $"No customer found matching '{plan.CustomerName ?? "that name"}'."
                    : string.Join("; ", customers.Take(8).Select(c =>
                    {
                        var mine = orders.Where(o => o.CustomerPhone == c.Phone).ToList();
                        return $"{c.Name} ({c.Phone}) has {mine.Count} order(s), ৳{mine.Sum(o => o.TotalAmount):0} total and ৳{mine.Sum(o => o.TotalAmount - o.PaidAmount):0} due";
                    })) + ".";
                return new FactSet(text, orders);
            }

            default:
            {
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId)
                    .OrderByDescending(o => o.CreatedAt).Take(8).ToListAsync(ct);
                return new FactSet(
                    $"The shop currently has {orders.Count} recent order(s) on record. Ask about unpaid bills, pickup delays, customers or collections for more detail.",
                    orders);
            }
        }
    }

    private static List<Order> Filter(List<Order> orders, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return orders;
        var term = name.ToLowerInvariant();
        return orders.Where(o => o.CustomerName.ToLower().Contains(term)).ToList();
    }

    private static string BuildContext(FactSet facts, List<DocumentMatch> matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Facts computed directly from the shop database:");
        sb.AppendLine(facts.Summary);
        if (matches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Semantically similar records retrieved from the vector store:");
            foreach (var match in matches.Take(6))
                sb.AppendLine($"- [{match.DocType}, similarity {match.Similarity:0.00}] {Truncate(match.Content, 260)}");
        }
        return sb.ToString();
    }

    private static string ComposeLocalAnswer(FactSet facts, List<DocumentMatch> matches, QueryPlan plan)
    {
        var answer = facts.Summary;
        if (plan.Intent == "general" && matches.Count > 0)
        {
            answer += " Related records: " + string.Join("; ", matches.Take(2).Select(m => Truncate(m.Content, 120)));
        }
        return answer;
    }

    private static List<AssistantSourceDto> BuildSources(List<DocumentMatch> matches)
    {
        var seen = new HashSet<string>();
        var sources = new List<AssistantSourceDto>();
        foreach (var match in matches)
        {
            var key = $"{match.DocType}:{match.RefId}";
            if (!seen.Add(key)) continue;
            var orderNo = Regex.Match(match.Content, @"#(WT-\d+)");
            var title = orderNo.Success
                ? $"Order {orderNo.Groups[1].Value}"
                : match.DocType == "customer" ? "Customer record" : "Shop record";
            sources.Add(new AssistantSourceDto
            {
                Type = match.DocType,
                RefId = match.RefId,
                Title = title,
                Snippet = Truncate(match.Content, 150),
                Similarity = Math.Round(match.Similarity, 3)
            });
            if (sources.Count >= 5) break;
        }
        return sources;
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty
        : text.Length <= max ? text : text[..max] + "…";

    private static QueryPlan ParseIntent(string question)
    {
        var lower = question.ToLowerInvariant();
        var daysMatch = Regex.Match(lower, @"(\d+)\s*(?:days?|din|d)\b");
        var days = daysMatch.Success && int.TryParse(daysMatch.Groups[1].Value, out var parsed) ? parsed : (int?)null;
        var name = ExtractName(question);
        var monthOnly = Regex.IsMatch(lower, "this month|এই মাসে|এ মাসে|মাসের");

        if (Regex.IsMatch(lower, @"\b(unpaid|due|baki|baaki|bill|bille?|payment|not paid|deya|dena|ostad)\b") ||
            Regex.IsMatch(lower, "বাকি|বাকী|অপরিশোধিত|বিল|বিলের"))
            return new QueryPlan("unpaid", days, name, monthOnly);
        if (Regex.IsMatch(lower, @"\b(pick ?up|picked|collect|delivery|tola|waiting|ready|porchha|pore ?cha|kapor|kapur|joma|dhore)\b") ||
            Regex.IsMatch(lower, "তোলা|কাপড়|পোশাক|অপেক্ষা|প্রস্তুত"))
            return new QueryPlan("pickup", days, name, monthOnly);
        if (Regex.IsMatch(lower, @"\b(revenue|collected|sales|income|money|taka|earnings?)\b") ||
            Regex.IsMatch(lower, "আয়|সংগৃহীত|টাকা"))
            return new QueryPlan("revenue", days, name, monthOnly);
        if (Regex.IsMatch(lower, @"\b(how many|koto|koy|count|total orders?|number of orders?)\b") ||
            Regex.IsMatch(lower, "কত|কয়টি|কতটি|অর্ডার"))
            return new QueryPlan("count", days, name, monthOnly);
        if (name != null || Regex.IsMatch(lower, @"\b(customer|kustomar|client)\b") ||
            Regex.IsMatch(lower, "গ্রাহক|কাস্টমার"))
            return new QueryPlan("customer", days, name, monthOnly);
        if (Regex.IsMatch(lower, @"\b(pending|progress|washing|ironing|active|ongoing)\b"))
            return new QueryPlan("active", days, name, monthOnly);

        return new QueryPlan("general", days, name, monthOnly);
    }

    private static string? ExtractName(string question)
    {
        var match = Regex.Match(question, @"\b(?:mr|mrs|ms|miss)\.?\s+([A-Za-z]+)", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;
        match = Regex.Match(question, @"\b(?:for|of|customer)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private sealed record QueryPlan(string Intent, int? Days, string? CustomerName, bool MonthOnly = false);
    private sealed record FactSet(string Summary, List<Order> Orders);
}

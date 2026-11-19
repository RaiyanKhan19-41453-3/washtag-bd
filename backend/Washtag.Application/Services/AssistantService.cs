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

        // Hashed offline embeddings cannot reliably separate relevant from unrelated
        // documents, so never present them as cited sources.
        var offline = _ai.UsedLocalEmbedding || _ai.Name.StartsWith("Local", StringComparison.OrdinalIgnoreCase);
        var sources = offline ? new List<AssistantSourceDto>() : BuildSources(matches);
        var answer = string.IsNullOrWhiteSpace(phrased)
            ? ComposeLocalAnswer(facts, matches, plan, offline)
            : phrased.Trim();
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
            SourcesJson = sources.Count > 0 ? JsonSerializer.Serialize(sources) : null,
            Intent = plan.Intent,
            AiPhrased = result.AiPhrased,
            Provider = result.Provider,
            SuggestionsJson = JsonSerializer.Serialize(Suggestions)
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
            Intent = m.Intent ?? string.Empty,
            AiPhrased = m.AiPhrased,
            Provider = m.Provider ?? string.Empty,
            Suggestions = string.IsNullOrEmpty(m.SuggestionsJson)
                ? Suggestions
                : JsonSerializer.Deserialize<List<string>>(m.SuggestionsJson) ?? Suggestions,
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

        List<Order> InRange(List<Order> list) => list.Where(o =>
            (!plan.FromUtc.HasValue || o.CreatedAt >= plan.FromUtc.Value) &&
            (!plan.ToUtc.HasValue || o.CreatedAt < plan.ToUtc.Value)).ToList();

        var period = !plan.FromUtc.HasValue ? "in total"
            : plan.ToUtc.HasValue ? "for the selected day"
            : plan.FromUtc.Value > now.AddDays(-2) ? "today"
            : plan.FromUtc.Value > now.AddDays(-8) ? "in the last 7 days"
            : "this month";

        switch (plan.Intent)
        {
            case "unpaid":
            {
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId && o.Status != OrderStatus.Cancelled &&
                                o.PaymentStatus != PaymentStatus.Paid)
                    .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
                orders = InRange(orders);
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
                orders = Filter(orders, plan.CustomerName);
                var orderIds = orders.Select(o => o.Id).ToList();
                var payments = await _db.Payments.AsNoTracking()
                    .Where(p => orderIds.Contains(p.OrderId) &&
                                (!plan.FromUtc.HasValue || p.CreatedAt >= plan.FromUtc.Value) &&
                                (!plan.ToUtc.HasValue || p.CreatedAt < plan.ToUtc.Value))
                    .ToListAsync(ct);
                var collected = payments.Sum(p => p.Amount);
                var paidOrderIds = payments.Select(p => p.OrderId).ToHashSet();
                var count = orders.Count(o => paidOrderIds.Contains(o.Id));
                var outstanding = orders.Sum(o => o.TotalAmount - o.PaidAmount);
                return new FactSet(
                    $"Amount collected {period} is ৳{collected:0} across {count} order(s). Outstanding balance: ৳{outstanding:0}.",
                    orders);
            }

            case "count":
            case "active":
            {
                var allOrders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId).ToListAsync(ct);
                var totalCount = InRange(allOrders).Count;
                var orders = InRange(allOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList());
                var active = orders.Count(o => o.Status is OrderStatus.Received or OrderStatus.Washing
                    or OrderStatus.Ironing or OrderStatus.ReadyForPickup);
                var ready = orders.Count(o => o.Status == OrderStatus.ReadyForPickup);
                var delivered = orders.Count(o => o.Status == OrderStatus.Delivered);
                var text = plan.Intent == "active"
                    ? $"There are {active} active order(s), including {ready} ready for pickup. Delivered so far: {delivered}."
                    : $"The shop has {totalCount} total order(s): {active} active and {delivered} delivered.";
                return new FactSet(text, orders.Where(o => o.Status != OrderStatus.Delivered).ToList());
            }

            case "customer":
            {
                var customers = await _db.Customers.AsNoTracking().Where(c => c.ShopId == shopId).ToListAsync(ct);
                if (!string.IsNullOrEmpty(plan.CustomerName))
                    customers = customers.Where(c => c.Name.ToLower().Contains(plan.CustomerName.ToLower())).ToList();
                var orders = await _db.Orders.AsNoTracking()
                    .Where(o => o.ShopId == shopId && o.Status != OrderStatus.Cancelled).ToListAsync(ct);
                orders = InRange(orders);
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

    private static string ComposeLocalAnswer(FactSet facts, List<DocumentMatch> matches, QueryPlan plan, bool offline)
    {
        var answer = facts.Summary;
        if (!offline && plan.Intent == "general" && matches.Count > 0)
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
        var now = DateTime.UtcNow;
        var localNow = now.AddHours(6); // Bangladesh UTC+6
        DateTime? from = days.HasValue ? now.AddDays(-days.Value) : null;
        DateTime? to = null;

        if (Regex.IsMatch(lower, @"\btoday\b|আজ"))
        {
            var start = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(-6);
            from = start; to = start.AddDays(1);
        }
        else if (Regex.IsMatch(lower, @"\byesterday\b|গতকাল"))
        {
            var todayStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(-6);
            from = todayStart.AddDays(-1); to = todayStart;
        }
        else if (Regex.IsMatch(lower, @"\bthis month\b|এই মাসে|এ মাসে|মাসের"))
        {
            from = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-6);
        }
        else if (Regex.IsMatch(lower, @"\b(last week|this week)\b|গত সপ্তাহ|এই সপ্তাহ"))
        {
            from = now.AddDays(-7);
        }

        if (Regex.IsMatch(lower, @"\b(unpaid|due|baki|baaki|bill|bille?|payment|not paid|haven'?t paid|deya|dena|ostad)\b") ||
            Regex.IsMatch(lower, "বাকি|বাকী|অপরিশোধিত|বিল|বিলের"))
            return new QueryPlan("unpaid", days, name, from, to);
        if (Regex.IsMatch(lower, @"\b(pick ?up|picked|collect|delivery|tola|waiting|ready|porchha|pore ?cha|kapor|kapur|joma|dhore)\b") ||
            Regex.IsMatch(lower, "তোলা|কাপড়|পোশাক|অপেক্ষা|প্রস্তুত"))
            return new QueryPlan("pickup", days, name, from, to);
        if (Regex.IsMatch(lower, @"\b(revenue|collect(?:ed|ion|ions)?|sales|income|money|taka|earnings?)\b") ||
            Regex.IsMatch(lower, "আয়|সংগৃহীত|টাকা"))
            return new QueryPlan("revenue", days, name, from, to);
        if (Regex.IsMatch(lower, @"\b(how many|koto|koy|count|total orders?|number of orders?|delivered)\b") ||
            Regex.IsMatch(lower, "কত|কয়টি|কতটি|অর্ডার|ডেলিভারি"))
            return new QueryPlan("count", days, name, from, to);
        if (name != null || Regex.IsMatch(lower, @"\b(customer|kustomar|client)\b") ||
            Regex.IsMatch(lower, "গ্রাহক|কাস্টমার"))
            return new QueryPlan("customer", days, name, from, to);
        if (Regex.IsMatch(lower, @"\b(pending|progress|washing|ironing|active|ongoing)\b"))
            return new QueryPlan("active", days, name, from, to);

        return new QueryPlan("general", days, name, from, to);
    }

    private static string? ExtractName(string question)
    {
        var match = Regex.Match(question, @"\b(?:mr|mrs|ms|miss)\.?\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;
        match = Regex.Match(question, @"\b(?:for|of|customer)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;
        // Banglish: "Tasnim Rahman er baki?" — name appears before the Bengali possessive.
        match = Regex.Match(question, @"^([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+er\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private sealed record QueryPlan(string Intent, int? Days, string? CustomerName,
        DateTime? FromUtc = null, DateTime? ToUtc = null);
    private sealed record FactSet(string Summary, List<Order> Orders);
}

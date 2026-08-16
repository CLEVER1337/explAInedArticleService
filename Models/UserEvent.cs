public sealed record UserEvent(
    string EventId,
    string EventType,
    string UserId,
    string ArticleId,
    DateTime OccurredAt,
    string Source,
    IDictionary<string, object?>? Metadata);

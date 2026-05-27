public class OutboxEvent
{
    public long Id { get; set; }
    public string EventType { get; set; } = default!;
    public string AggregateId { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

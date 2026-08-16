public class KafkaSettings
{
    public string BootstrapServers { get; set; } = default!;
    public string Topic { get; set; } = default!;
    public string ConsumerGroupId { get; set; } = default!;
    public int PublisherPollMs { get; set; } = 1000;
    public int PublisherBatchSize { get; set; } = 100;
    public string UserEventsTopic { get; set; } = "user.events";
}

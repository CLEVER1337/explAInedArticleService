using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public class OutboxPublisherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    private readonly ILogger<OutboxPublisherHostedService> _logger;

    public OutboxPublisherHostedService(
        IServiceScopeFactory scopeFactory,
        IProducer<string, string> producer,
        IOptions<KafkaSettings> settings,
        ILogger<OutboxPublisherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(_settings.PublisherPollMs, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox publisher iteration failed");
                await Task.Delay(_settings.PublisherPollMs, stoppingToken);
            }
        }
    }

    private async Task<int> DrainBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await db.OutboxEvents
            .Where(o => o.PublishedAt == null)
            .OrderBy(o => o.Id)
            .Take(_settings.PublisherBatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        foreach (var evt in pending)
        {
            var message = new Message<string, string>
            {
                Key = evt.AggregateId,
                Value = evt.Payload,
                Headers = new Headers
                {
                    new Header("eventType", System.Text.Encoding.UTF8.GetBytes(evt.EventType))
                }
            };

            try
            {
                await _producer.ProduceAsync(_settings.Topic, message, ct);
                evt.PublishedAt = DateTime.UtcNow;
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Failed to publish outbox event {EventId} to Kafka", evt.Id);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}

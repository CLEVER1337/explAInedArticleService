using Confluent.Kafka;
using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public class ElasticsearchIndexerHostedService : BackgroundService
{
    private const string IndexName = "articles";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ElasticsearchClient _es;
    private readonly KafkaSettings _settings;
    private readonly ILogger<ElasticsearchIndexerHostedService> _logger;

    public ElasticsearchIndexerHostedService(
        IServiceScopeFactory scopeFactory,
        ElasticsearchClient es,
        IOptions<KafkaSettings> settings,
        ILogger<ElasticsearchIndexerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _es = es;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_settings.Topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error");
                    continue;
                }

                if (result?.Message == null) continue;

                var aggregateId = result.Message.Key;
                var eventType = ExtractEventType(result.Message.Headers);

                try
                {
                    await HandleEventAsync(eventType, aggregateId, stoppingToken);
                    consumer.Commit(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to index article {ArticleId} (event {EventType}); offset not committed", aggregateId, eventType);
                    await Task.Delay(_settings.PublisherPollMs, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleEventAsync(string eventType, string aggregateId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var article = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aggregateId, ct);
        if (article == null)
        {
            var delete = await _es.DeleteAsync(aggregateId, d => d.Index(IndexName), ct);
            if (!delete.IsValidResponse && delete.Result != Elastic.Clients.Elasticsearch.Result.NotFound)
            {
                throw new Exception($"Failed to delete article {aggregateId} from Elasticsearch: {delete.DebugInformation}");
            }
            return;
        }

        var response = await _es.IndexAsync(article, i => i.Index(IndexName).Id(article.Id), ct);
        if (!response.IsValidResponse)
        {
            throw new Exception($"Failed to index article {aggregateId}: {response.DebugInformation}");
        }
    }

    private static string ExtractEventType(Headers headers)
    {
        if (headers == null) return string.Empty;
        if (headers.TryGetLastBytes("eventType", out var bytes))
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        return string.Empty;
    }
}

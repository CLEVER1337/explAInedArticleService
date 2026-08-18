using Confluent.Kafka;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var dbProvider = configuration["Database:Provider"] ?? "Postgres";

    if (string.Equals(dbProvider, "InMemory", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase(
            configuration["Database:InMemoryName"] ?? "explAIned-articles-tests");
    }
    else
    {
        options.UseNpgsql(configuration["ConnectionStrings:PostgreSQL"]);
    }
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<ElasticsearchClient>(sp => {
    var settings = new ElasticsearchClientSettings(new Uri(builder.Configuration["Elasticsearch:Url"]))
    .DefaultIndex("articles")
    .Authentication(new BasicAuthentication(builder.Configuration["Elasticsearch:Username"], builder.Configuration["Elasticsearch:Password"]))
    .ServerCertificateValidationCallback((_, _, _, _) => true);
    return new ElasticsearchClient(settings);
});

builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));

builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"],
        Acks = Acks.All,
        EnableIdempotence = true,
    };
    return new ProducerBuilder<string, string>(producerConfig).Build();
});

builder.Services.AddSingleton<IUserEventProducer, UserEventProducer>();

builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddHostedService<ElasticsearchIndexerHostedService>();

builder.Services.AddOptions();
builder.Services.AddSingleton<MemoryDistributedCache>();
builder.Services.AddSingleton<RedisCache>();
builder.Services.Configure<RedisCacheOptions>(options =>
{
    options.Configuration = builder.Configuration["ConnectionStrings:Redis"];
    options.InstanceName = "explAIned_";
});

builder.Services.AddSingleton<IDistributedCache>(sp =>
{
    var cacheProvider = sp.GetRequiredService<IConfiguration>()["Cache:Provider"] ?? "Redis";

    return string.Equals(cacheProvider, "Memory", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<MemoryDistributedCache>()
        : sp.GetRequiredService<RedisCache>();
});

builder.Services.AddScoped<CacheService>();
builder.Services.AddScoped<IArticleService, ArticleService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseHttpMetrics();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapMetrics();

app.Run();

public partial class Program;

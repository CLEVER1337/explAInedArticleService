using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

var builder = WebApplication.CreateBuilder(args);

// json config
builder.Configuration.AddJsonFile("appsettings.json");

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<ElasticsearchClient>(sp => {
    var settings = new ElasticsearchClientSettings(new Uri(builder.Configuration.GetConnectionString("Elasticsearch:Url")))
    .DefaultIndex("articles")
    .Authentication(new BasicAuthentication(builder.Configuration.GetConnectionString("Elasticsearch:Username"), builder.Configuration.GetConnectionString("Elasticsearch:Password")));
    return new ElasticsearchClient(settings);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

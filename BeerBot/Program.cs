using BeerBot;
using BeerBot.Commands;
using BeerBot.Data;
using BeerBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// Telegram bot client (singleton)
builder.Services.AddSingleton<ITelegramBotClient>(_ =>
    new TelegramBotClient(builder.Configuration["Telegram:BotToken"]!));

// HttpClient for OpenRouter
builder.Services.AddHttpClient("openrouter");

// Database (scoped per request via scope factory)
builder.Services.AddDbContext<BeerBotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Services
builder.Services.AddSingleton<OpenRouterClient>();
builder.Services.AddSingleton<OverlapFinder>();

// Scoped command handlers (need DbContext)
builder.Services.AddScoped<BeertimeCommand>();
builder.Services.AddScoped<StatusCommand>();
builder.Services.AddScoped<SuggestCommand>();
builder.Services.AddScoped<BotUpdateHandler>();

// Background services
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<SchedulerService>();

var host = builder.Build();
host.Run();

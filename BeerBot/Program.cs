using BeerBot;
using BeerBot.Commands;
using BeerBot.Data;
using BeerBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(
    builder.Configuration["Telegram:BotToken"]!
));

builder.Services.AddDbContext<BeerBotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
);

builder.Services.AddSingleton<OverlapFinder>();

builder.Services.AddScoped<SlotWizard>();
builder.Services.AddScoped<BeertimeCommand>();
builder.Services.AddScoped<StatusCommand>();
builder.Services.AddScoped<SuggestCommand>();
builder.Services.AddScoped<BotUpdateHandler>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<SchedulerService>();

IHost host = builder.Build();

// Apply any pending EF Core migrations before the bot starts polling, so a fresh
// database in a new environment is created with the right schema. Single-instance
// bot, so running this at startup is safe (no concurrent migrators).
using (IServiceScope scope = host.Services.CreateScope())
{
    BeerBotDbContext db = scope.ServiceProvider.GetRequiredService<BeerBotDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();

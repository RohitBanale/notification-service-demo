using Microsoft.EntityFrameworkCore;
using NotificationServiceDemo.Api.Data;
using NotificationServiceDemo.Api.Services;
using NotificationServiceDemo.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<DispatcherOptions>(builder.Configuration.GetSection("Dispatcher"));
builder.Services.Configure<SummaryOptions>(builder.Configuration.GetSection("Summary"));

builder.Services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

builder.Services.AddScoped<INotificationProvider, FakeNotificationProvider>();   // swap for a real provider
builder.Services.AddScoped<NotificationProcessor>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddHostedService<DispatcherWorker>();
builder.Services.AddHostedService<DailySummaryWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();

public partial class Program { }   // exposed for integration tests

using tikkaman.sreSandbox.Controllers;
using tikkaman.sreSandbox.Middleware;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.RollingFile;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Logger(lc => lc
        .Filter.ByExcluding(Matching.FromSource("tikkaman.sreSandbox.Middleware.RequestLoggingMiddleware"))
        .WriteTo.File(
            System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory,
                "LogFiles", "Application", "diagnostics.txt"),
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            retainedFileCountLimit: 2,
            rollOnFileSizeLimit: true,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1)))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.FromSource("tikkaman.sreSandbox.Middleware.RequestLoggingMiddleware"))
        .WriteTo.File(
            System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory,
                "LogFiles", "Application", "access.log"),
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            retainedFileCountLimit: 2,
            rollOnFileSizeLimit: true,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1),
            outputTemplate: "{Message:lj}{NewLine}"))
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", 
                                optional: false, 
                                reloadOnChange: true);

builder.Services.AddSerilog();

//Disable the FailedRequestBlocker on Controller level
builder.Services.AddScoped<RequestBlockingEnabledConfig>();
builder.Services.AddTransient(ctx =>
            new ResponderTestController(
                builder.Configuration,
                new RequestBlockingEnabledConfig(false)));

//Enable the FailedRequestBlocker middleware cleanup service
builder.Services.AddScoped<FailedRequestCleanupService>();
builder.Services.AddSingleton<PeriodicCleanupService>();
builder.Services.AddHostedService(
    provider => provider.GetRequiredService<PeriodicCleanupService>());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => {
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Apache Access Log parser & Failed Request blocker", Version = "v0.1" });
    c.SchemaGeneratorOptions.UseAllOfToExtendReferenceSchemas = true;
    c.DescribeAllParametersInCamelCase();
    c.UseInlineDefinitionsForEnums();
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}
// app.UseRouting();
app.UseAuthorization();

//Enable the FailedRequestBlocker middleware
app.UseRequestLogging();
app.UseFailedRequestBlocker();

app.MapControllers();

app.Run();
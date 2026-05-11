using MCPoe.Application.Interfaces;
using MCPoe.Infrastructure.Database;
using MCPoe.Infrastructure.ModsDb;
using MCPoe.Infrastructure.PoB;
using MCPoe.Infrastructure.Wiki;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var logPath = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .Build()["Logging:LogPath"] ?? "logs/mcpoe-.log";

var logDir = Path.GetDirectoryName(logPath);
if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
{
    Directory.CreateDirectory(logDir);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    Log.Error(args.ExceptionObject as Exception, "Unhandled exception in AppDomain");
};

TaskScheduler.UnobservedTaskException += (_, args) =>
{
    Log.Error(args.Exception, "Unobserved task exception");
    args.SetObserved();
};

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.AddSingleton<IWikiSearchService, WikiSearchService>();
    builder.Services.AddSingleton<IModsDbService, ModsDbService>();
    builder.Services.AddSingleton<IPoBService, PoBService>();
    builder.Services.AddSingleton<DatabaseInitializer>();

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    using var host = builder.Build();

    host.Services.GetRequiredService<DatabaseInitializer>().Initialize();

    Log.Information("MCPoe server starting (stdio transport)");
    await host.RunAsync();
    Log.Information("MCPoe server stopped");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCPoe server terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

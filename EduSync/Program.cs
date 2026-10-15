using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using EduSync.Infrastructure;
using EduSync.Repositories;
using EduSync.Services;

EduSync.Logging.LoggerConfiguration.Configure();

try
{
    Log.Information("EduSync Started");

    var builder = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((ctx, cfg) =>
        {
            // Host.CreateDefaultBuilder already loads appsettings.json, this is optional
            cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        })
        .ConfigureServices((ctx, services) =>
        {
            // IConfiguration is provided by the Host
            services.AddSingleton<SourceDbConnectionFactory>();
            services.AddSingleton<TargetDbConnectionFactory>();
            services.AddSingleton<SyncHashRepository>();

            // Register the sync service you want to run
            services.AddTransient<DepartmentSyncService>();
            services.AddTransient<SubDepartmentSyncService>();
            services.AddTransient<CampusSyncService>();
            services.AddTransient<AcademicYearSyncService>();
            services.AddTransient<SemesterSyncService>();
            services.AddTransient<InstructorSyncService>();
            services.AddTransient<StudentSyncService>();
        });

    using var host = builder.Build();
    await host.StartAsync();

    var command = args.FirstOrDefault();

    Console.WriteLine($"Command: {command}");

    using var scope = host.Services.CreateScope();

    switch (command)
    {
        case "departments":
            await scope.ServiceProvider
            .GetRequiredService<DepartmentSyncService>()
            .SyncAsync();
            break;

        case "instructors":
            await scope.ServiceProvider
                .GetRequiredService<InstructorSyncService>()
                .SyncAsync();
            break;
        
        case "students":
            await scope.ServiceProvider
                .GetRequiredService<InstructorSyncService>()
                .SyncAsync();
            break;

        case "all":
            await scope.ServiceProvider
                .GetRequiredService<DepartmentSyncService>()
                .SyncAsync();

            await scope.ServiceProvider
                .GetRequiredService<SubDepartmentSyncService>()
                .SyncAsync();

            await scope.ServiceProvider
                .GetRequiredService<CampusSyncService>()
                .SyncAsync();

            await scope.ServiceProvider
                .GetRequiredService<AcademicYearSyncService>()
                .SyncAsync();

            await scope.ServiceProvider
                .GetRequiredService<SemesterSyncService>()
                .SyncAsync();

            await scope.ServiceProvider
                .GetRequiredService<InstructorSyncService>()
                .SyncAsync();
           
            await scope.ServiceProvider
                .GetRequiredService<StudentSyncService>()
                .SyncAsync();
            break;
    }

    await host.StopAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application crashed");
    throw;
}
finally
{
    EduSync.Logging.LoggerConfiguration.Close();
    Log.CloseAndFlush();
}
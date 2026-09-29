using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Logging
{
    public static class LoggerConfiguration
    {
        public static void Configure()
        {
            Directory.CreateDirectory("logs");

            Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Information()

                .MinimumLevel.Override(
                    "Microsoft",
                    LogEventLevel.Warning)

                .Enrich.FromLogContext()

                .WriteTo.Console(

                    outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss}] " +
                    "[{Level:u3}] " +
                    "{Message:lj}" +
                    "{NewLine}" +
                    "{Exception}"

                )

                .WriteTo.File(

                    path: "logs/sync-.log",

                    rollingInterval:
                        RollingInterval.Day,

                    retainedFileCountLimit: 30,

                    shared: true,

                    outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss}] " +
                    "[{Level:u3}] " +
                    "{Message:lj}" +
                    "{NewLine}" +
                    "{Exception}"

                )

                .CreateLogger();
        }

        public static void Close()
        {
            Log.CloseAndFlush();
        }
    }
}

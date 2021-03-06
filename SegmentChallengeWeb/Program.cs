using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SegmentChallengeWeb {
    public static class Program {
        public static void Main(string[] args) {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateWebHostBuilder(string[] args) {
            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(builder => {
                    builder.ConfigureAppConfiguration((hostContext, config) => {
                            var environmentName =
                                hostContext.HostingEnvironment.EnvironmentName;
                            config
                                .AddJsonFile("appsettings.json", optional: true)
                                .AddJsonFile($"{Environment.GetEnvironmentVariable("CONFIG_PATH")  ?? "config"}/appsettings.{environmentName}.json", optional: false)
                                .AddEnvironmentVariables()
                                .AddCommandLine(args);
                        })
                        .ConfigureLogging((context, logging) => {
                            logging.AddConfiguration(context.Configuration.GetSection("Logging"))
                                .AddConsole(options => {
                                    // Send all log messages to stderr
                                    options.LogToStandardErrorThreshold = LogLevel.Trace;
                                });
                        })
                        .ConfigureKestrel((context, options) => {
                            options.Configure(context.Configuration.GetSection("Kestrel"));
                        })
                        .UseStartup<Startup>();
                });
        }
    }
}

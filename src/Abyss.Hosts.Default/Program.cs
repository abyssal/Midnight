using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AbyssalSpotify;
using Disqord;
using Disqord.Bot;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qmmands;

namespace Abyss.Hosts.Default
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var dataRoot = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            if (!Directory.Exists(dataRoot)) dataRoot = AppDomain.CurrentDomain.BaseDirectory; // IIS tomfoolery

            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(dataRoot);
                    config.AddJsonFile("abyss.json", false, true);
                    config.AddJsonFile($"abyss.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true);
                })
                .ConfigureServices(serviceColl =>
                {
                    // add Abyss framework
                    serviceColl.AddAbyssFramework((provider, botOptions) =>
                    {
                        botOptions.DataRoot = dataRoot;
                    });

                    // add services required by Abyss.Commands.Default
                    serviceColl.AddSingleton(provider =>
                    {
                        var configurationModel = provider.GetRequiredService<AbyssConfig>();
                        return SpotifyClient.FromClientCredentials(configurationModel.Connections.Spotify.ClientId, configurationModel.Connections.Spotify.ClientSecret);
                    });
                    serviceColl.AddTransient<Random>();
                })
                .UseDefaultServiceProvider(c =>
                {
                    c.ValidateOnBuild = true;
                    c.ValidateScopes = true;
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.SuppressStatusMessages(false);
                    webBuilder.CaptureStartupErrors(true);
                    webBuilder.UseSetting(WebHostDefaults.ApplicationKey, "Abyss");
                    webBuilder.ConfigureKestrel(kestrel =>
                    {
                        kestrel.ListenAnyIP(2110);
                    });
                    webBuilder.UseStartup<Startup>();
                });
        }
    }
}

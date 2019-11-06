using System;
using System.IO;
using System.Reflection;
using Abyss.Commands.Default;
using AbyssalSpotify;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

            return new HostBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(dataRoot);
                    config.AddJsonFile("abyss.json", false, true);
                    config.AddJsonFile($"abyss.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true);
                })
                .ConfigureServices(serviceColl => ConfigureServices(dataRoot, serviceColl))
                .UseDefaultServiceProvider(c =>
                {
                    c.ValidateOnBuild = true;
                    c.ValidateScopes = true;
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                });
        }

        public static void ConfigureServices(string dataRoot, IServiceCollection services)
        {
            // Configuration
            services.AddSingleton(p =>
            {
                var ob = new AbyssConfig();
                p.GetRequiredService<IConfiguration>().Bind(ob);
                return ob;
            });

            // Abyss framework
            services.AddAbyssFramework((provider, botOptions) =>
            {
                botOptions.DataRoot = dataRoot;
            });
            services.AddSingleton<IPackLoader, DefaultPackLoader>();

            // Abyss.Commands.Default
            services.AddSingleton(provider =>
            {
                var configurationModel = provider.GetRequiredService<AbyssConfig>();
                return SpotifyClient.FromClientCredentials(configurationModel.Connections.Spotify.ClientId, configurationModel.Connections.Spotify.ClientSecret);
            });

            services.AddTransient<Random>();
        }
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Abyss.Parsers;
using Abyss.Persistence.Relational;
using Disqord;
using Disqord.Bot;
using Disqord.Gateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qmmands;

namespace Abyss
{
    public class Abyss : DiscordBot
    {
        private readonly IServiceScope _scope;
        public Abyss(IOptions<DiscordBotConfiguration> options, ILogger<Abyss> logger, IServiceProvider services,
            DiscordClient client) : base(options, logger, services, client)
        {
            Commands.CommandExecuted += CommandExecutedAsync;
            _scope = Services.CreateScope();
            JoinedGuild += (_, _) => { UpdateGuildStatistics(); return ValueTask.CompletedTask;};
            LeftGuild += (_, _) => { UpdateGuildStatistics(); return ValueTask.CompletedTask;};
            Ready += (_, _) =>
            {
                UpdateGuildStatistics();
                return ValueTask.CompletedTask;
            };
            GuildAvailable += (_, _) => { UpdateGuildStatistics(); return ValueTask.CompletedTask;};
            GuildUnavailable += (_, _) => { UpdateGuildStatistics(); return ValueTask.CompletedTask;};
        }

        private void UpdateGuildStatistics()
        {
            AbyssStatistics.CachedGuilds.Set(this.GetGuilds().Count);
        }

        private async ValueTask CommandExecutedAsync(object sender, CommandExecutedEventArgs e)
        {
            AbyssStatistics.TotalCommandsExecuted.WithLabels(e.Result?.IsSuccessful.ToString() ?? "unknown").Inc();
            var database = _scope.ServiceProvider.GetRequiredService<AbyssDatabaseContext>();
            var record = await database
                .GetUserAccountAsync((e.Context as DiscordCommandContext).Author.Id);
            record.LatestInteraction = DateTimeOffset.Now;
            record.FirstInteraction ??= DateTimeOffset.Now;
            await database.SaveChangesAsync();
        }

        protected override ValueTask AddTypeParsersAsync(CancellationToken cancellationToken = new())
        {
            Commands.AddTypeParser(new UriTypeParser());
            return base.AddTypeParsersAsync(cancellationToken);
        }

        protected override void MutateModule(ModuleBuilder moduleBuilder)
        {
            foreach (var command in moduleBuilder.Commands
                .Where(command => command.Parameters.Count > 0 && !command.Parameters[^1].IsMultiple))
            {
                command.Parameters[^1].IsRemainder = true;
            }

            base.MutateModule(moduleBuilder);
        }

        protected override async ValueTask AddModulesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            await _scope.ServiceProvider.GetRequiredService<AbyssDatabaseContext>().Database.MigrateAsync(cancellationToken);
            await base.AddModulesAsync(cancellationToken);
        }
    }
}
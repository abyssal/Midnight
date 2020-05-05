﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Abyssal.Common;
using Disqord;
using Disqord.Bot;
using Disqord.Bot.Prefixes;
using Disqord.Events;
using Disqord.Logging;
using Humanizer;
using Qmmands;
using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace Adora
{
    public class AdoraBot : DiscordBot
    {
        public int CommandSuccesses { get; private set; }
        public int CommandFailures { get; private set; }

        public const string ZeroWidthSpace = "​";
        public static readonly Color SystemColor = new Color(0xB2F7EF);

        private readonly ILogger _logger;
        private readonly HelpService _help;
        private readonly AdoraConfig _config;

        internal readonly IServiceProvider Services;

        public override object GetService(Type serviceType) => Services.GetService(serviceType);

        public AdoraBot(AdoraConfig config, DiscordBotConfiguration botConfiguration, IPrefixProvider prefixes, IServiceProvider provider,
            HelpService help) : base(TokenType.Bot, config.Connections.Discord.Token, prefixes, botConfiguration)
        {
            Services = provider;
            _logger = Log.Logger.ForContext<AdoraBot>();
            _help = help;
            _config = config;

            CommandExecuted += HandleCommandExecutedAsync;
            CommandExecutionFailed += HandleCommandExecutionFailedAsync;
            AddModules(Assembly.GetExecutingAssembly());

            Ready += Discord_Ready;
            Logger.MessageLogged += DiscordClient_Log;
        }

        private void DiscordClient_Log(object? sender, MessageLoggedEventArgs arg)
        {
            LogEventLevel severity = arg.Severity switch
            {
                LogMessageSeverity.Error => LogEventLevel.Error,
                LogMessageSeverity.Critical => LogEventLevel.Fatal,
                LogMessageSeverity.Debug => LogEventLevel.Debug,
                LogMessageSeverity.Trace => LogEventLevel.Verbose,
                LogMessageSeverity.Warning => LogEventLevel.Warning,
                LogMessageSeverity.Information => LogEventLevel.Information,
                _ => LogEventLevel.Information
            };
            if (severity == LogEventLevel.Debug || severity == LogEventLevel.Verbose) return;
            Log.Logger.ForContext("SourceContext", $"Discord {arg.Source}").Write(severity, arg.Exception, arg.Message);
        }

        private Task Discord_Ready(ReadyEventArgs e)
        {
            _logger.Information("Ready. Time since start: {time}. Logged in as {CurrentUser}", (Process.GetCurrentProcess().StartTime - DateTime.Now).Humanize(), new
            {
                Name = CurrentUser.ToString(),
                Id = CurrentUser.Id.RawValue,
                Guilds = Guilds.Count,
                Prefix = _config.CommandPrefix,
                e.SessionId
            });

            var activities = new List<(ActivityType, string)>
            {
                (ActivityType.Watching, "over the cosmos")
            };

            Task.Run(async () =>
            {
                while (true)
                {
                    var (activityType, message) = activities.Random();
                    await SetPresenceAsync(UserStatus.Online, new LocalActivity(message, activityType));
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });

            return Task.CompletedTask;
        }

        public async Task HandleRuntimeExceptionAsync(AdoraCommandContext context, Exception exception, CommandExecutionStep step, string reason)
        {
            var command = context.Command;
            CommandFailures++;

            var embed = new LocalEmbedBuilder
            {
                Color = Color.Red,
                Title = "Internal error",
                Description = reason,
                ThumbnailUrl = context.Bot.CurrentUser.GetAvatarUrl(),
                Footer = new LocalEmbedFooterBuilder
                {
                    Text =
                        $"This (probably) shouldn't happen."
                },
                Timestamp = DateTimeOffset.Now
            };

            embed.AddField("Command", command.Name);
            embed.AddField("Pipeline step", step.Humanize());
            embed.AddField("Message", exception.Message);

            Log.ForContext(new CommandContextEnricher(context)).Error(exception, "Pipeline failed at step {Step}.", step);

            await context.Channel.SendMessageAsync(string.Empty, false, embed.Build()).ConfigureAwait(false);
        }

        public async Task HandleCommandExecutedAsync(CommandExecutedEventArgs args)
        {
            var result = args.Result;
            var ctx = args.Context;
            var command = ctx.Command;
            var context = ctx.AsAdoraContext();
            var logger = context.Logger; 

            if (!(result is AdoraResult baseResult))
            {
                if (result == null)
                {
                    logger.Error("Command {Name} returned a null result type.", command.Name);
                } else logger.Error("Command {Name} returned a result of type {TypeName} and not {ResultTypeName}.", command.Name, result.GetType().Name, typeof(AdoraResult).Name);
                await context.Channel.TrySendMessageAsync($"Man, this bot sucks. Command {command.Name} is broken, and will need to be recompiled. Try again later. (Developer: The command returned a type that isn't a {typeof(AdoraResult).Name}.)");
                return;
            }

            if (result.IsSuccessful) CommandSuccesses++;
            else CommandFailures++;

            try
            {
                await baseResult.ExecuteResultAsync(context).ConfigureAwait(false);
                var resultLogger = logger.ForContext("Result", baseResult.ToLog(), true);

                if (baseResult.IsSuccessful)
                {
                    resultLogger.Information("Completed successfully.");
                }
                else
                {
                    resultLogger.Error("Returned unsuccessful result.");
                }
            }
            catch (Exception e)
            {
                await HandleRuntimeExceptionAsync(context, e, CommandExecutionStep.Command, $"An exception of type {e.GetType().Name} was thrown.");
            }
        }

        public Task HandleCommandExecutionFailedAsync(CommandExecutionFailedEventArgs e)
        {
            return HandleRuntimeExceptionAsync(e.Context.AsAdoraContext(), e.Result.Exception, e.Result.CommandExecutionStep, e.Result.Reason);
        }

        protected override async ValueTask AfterExecutedAsync(IResult result, DiscordCommandContext rawContext)
        {
            var context = rawContext.AsAdoraContext();
            if (result.IsSuccessful)
            {
                if (!(result is SuccessfulResult)) CommandSuccesses++; // SuccessfulResult indicates a RunMode.Async
                return;
            }
            var logger = context.Logger;

            switch (result)
            {
                case CommandResult _:
                case ExecutionFailedResult _:
                    return;

                case CommandNotFoundResult _:
                    //logger.Information("No command found matching \"{content}\"", context.Message.Content);
                    break;

                case ChecksFailedResult cfr:
                    logger.Information("{count} checks failed on {type}", cfr.FailedChecks.Count, cfr.Command?.FullAliases[0] ?? cfr.Module.GetType().Name);

                    var checks = cfr.FailedChecks.Where(check => check.Check.GetType().CustomAttributes.All(a => a.AttributeType != typeof(SilentAttribute))).ToList();

                    if (checks.Count == 0) break;

                    await context.Channel.SendMessageAsync(embed: new LocalEmbedBuilder()
                        .WithTitle(
                            $"No can do.")
                        .WithDescription("Can't do that, because: \n" + string.Join("\n",
                            checks.Select(a => $"{(checks.Count == 1 ? "" : "- ")}{a.Result.Reason}")))
                        .WithColor(Color.Red)
                        .WithFooter(
                            $"{(cfr.Command == null ? $"Module {cfr.Module.Name}" : $"Command {cfr.Command.Name} in module {cfr.Command.Module.Name}")}, " +
                            $"executed by {context.Invoker.Format()}")
                        .WithCurrentTimestamp()
                        .Build()).ConfigureAwait(false);
                    break;

                case ParameterChecksFailedResult pcfr:
                    logger.Information("{count} parameter checks failed on {type} (command {command})", pcfr.FailedChecks.Count, pcfr.Parameter.Name, pcfr.Parameter.Command.Name);

                    var pchecks = pcfr.FailedChecks.Where(check => check.Check.GetType().CustomAttributes.All(a => a.AttributeType != typeof(SilentAttribute))).ToList();

                    if (pchecks.Count == 0) break;

                    await context.Channel.SendMessageAsync(embed: new LocalEmbedBuilder()
                        .WithTitle(
                            $"No can do.")
                        .WithDescription(string.Join("\n",
                            pchecks.Select(a => $"{(pchecks.Count == 1 ? "" : "- ")}{a.Result.Reason}")))
                        .WithColor(Color.Red)
                        .WithFooter(
                            $"Parameter {pcfr.Parameter.Name} in command {pcfr.Parameter.Command.Name} (module {pcfr.Parameter.Command.Module.Name}), executed by {context.Invoker.Format()}")
                        .WithCurrentTimestamp()
                        .Build()).ConfigureAwait(false);
                    break;

                case ArgumentParseFailedResult apfr0 when apfr0.ParserResult is DefaultArgumentParserResult apfr:
                    logger.Information("Parse failed for {commandName}. Reason: {reason}", apfr.Command.Name, apfr.Failure?.Humanize());
                    if (apfr.Failure != null && apfr.Failure == DefaultArgumentParserFailure.TooFewArguments)
                    {
                        await context.Channel.SendMessageAsync(string.Empty, false, (await _help.CreateCommandEmbedAsync(apfr.Command, context))
                            .WithCurrentTimestamp()
                            .WithColor(context.BotMember.GetHighestRoleColourOrSystem()).Build());
                        break;
                    }

                    await context.Channel.SendMessageAsync(
                        $"I couldn't read whatever you just said: {apfr.Failure?.Humanize() ?? "A parsing error occurred."}.").ConfigureAwait(false);
                    break;

                case ArgumentParseFailedResult apfr1 when apfr1.ParserResult is UnixArgumentParserResult upfr:
                    logger.Information("UNIX parse failed for {commandName}. Reason: {reason}", upfr.Context.Command.Name, upfr.ParseFailure?.Humanize());

                    await context.Channel.SendMessageAsync(
                        $"I couldn't read whatever you just said: {upfr.ParseFailure.Humanize()}.").ConfigureAwait(false);
                    break;
                case TypeParseFailedResult tpfr:
                    logger.Information("Failed to parse \"{text}\" as type {type} for parameter {parameter} in command {command}", tpfr.Value, tpfr.Parameter.Type.Name, tpfr.Parameter.Name, tpfr.Parameter.Command.FullAliases[0]);

                    var sb = new StringBuilder().AppendLine(tpfr.Reason);
                    sb.AppendLine();
                    sb.AppendLine($"**Expected:** {HelpService.GetFriendlyName(tpfr.Parameter, tpfr.Parameter.Service)}");
                    sb.AppendLine($"**Received:** {tpfr.Value}");
                    sb.AppendLine($"Try using {context.Prefix}help {tpfr.Parameter.Command.Name} for help on this command.");

                    await context.Channel.SendMessageAsync(sb.ToString()).ConfigureAwait(false);
                    break;

                case CommandOnCooldownResult cdr:
                    _logger.Information("Cooldown(s) activated for command {command}", cdr.Command.Name);

                    await context.Channel.SendMessageAsync($"Cooldown(s) activated for command {cdr.Command.Name}. " +
                        $"Try again in {cdr.Cooldowns.Select(a => a.RetryAfter).OrderByDescending(c => c.TotalSeconds).First().Humanize(20, maxUnit: Humanizer.Localisation.TimeUnit.Hour)}.");
                    break;

                case OverloadsFailedResult ofr:
                    _logger.Information("Overloads failed for text \"{content}\"", context.Message.Content);

                    await context.Channel.SendMessageAsync(embed: new LocalEmbedBuilder()
                        .WithTitle("There's multiple versions of this command available")
                        .WithDescription(
                            $"I tried to run the closest available version, but none of them worked. Here's why:")
                        .WithCurrentTimestamp()
                        .WithColor(Color.Red)
                        .WithFields(ofr.FailedOverloads.Select(ov =>
                        {
                            return new LocalEmbedFieldBuilder().WithName(ov.Key.CreateCommandString()).WithValue(ov.Value.Reason).WithIsInline(false);
                        }))
                        .WithRequesterFooter(context)
                        .Build()).ConfigureAwait(false);
                    break;

                default:
                    _logger.Error("Unknown result type \"{result}\"", result.GetType().Name);
                    break;
            }

            await base.AfterExecutedAsync(result, context);
        }

        protected override async ValueTask<bool> BeforeExecutedAsync(DiscordCommandContext context)
        {
            var b = await base.BeforeExecutedAsync(context);
            return b && context.Guild != null;
        }


        protected override ValueTask<DiscordCommandContext> GetCommandContextAsync(CachedUserMessage message, IPrefix prefix)
        {
            return new ValueTask<DiscordCommandContext>(new AdoraCommandContext(this, message, prefix));
        }
    }
}
using Disqord;
using Qmmands;
using System;
using System.Threading.Tasks;

namespace Abyss
{
    /// <summary>
    ///     A check which requires that the current argument must not be the bot.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class MustNotBeBotAttribute : ParameterCheckAttribute, IAbyssCheck
    {
        public override ValueTask<CheckResult> CheckAsync(object argument, CommandContext context)
        {
            if (argument == null) return CheckResult.Successful;
            var id = argument is CachedUser user ? user.Id : argument is Snowflake dur ? dur : throw new InvalidOperationException($"{nameof(MustNotBeBotAttribute)} is being executed on an invalid object type.");

            return id == context.AsAbyssContext().Bot.CurrentUser.Id
                ? new CheckResult("The provided user can't be me.")
                : CheckResult.Successful;
        }

        public string GetDescription(AbyssCommandContext requestContext) => "The provided user can't be me.";

        /// <summary>
        ///     Initialises a new <see cref="MustNotBeBotAttribute"/>.
        /// </summary>
        public MustNotBeBotAttribute() : base(CheckResources.UserTypes)
        {
        }
    }
}
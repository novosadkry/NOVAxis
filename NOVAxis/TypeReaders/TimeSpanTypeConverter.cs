using System;
using System.Threading.Tasks;

using NOVAxis.Utilities;

using Discord;
using Discord.Interactions;

namespace NOVAxis.TypeReaders
{
    public class TimeSpanTypeConverter : TypeConverter<TimeSpan>
    {
        public override ApplicationCommandOptionType GetDiscordType()
            => ApplicationCommandOptionType.String;

        public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services)
        {
            if (TimeSpanParser.TryParse((string)option.Value, out var result))
                return Task.FromResult(TypeConverterResult.FromSuccess(result));

            return Task.FromResult(TypeConverterResult.FromError(
                InteractionCommandError.ParseFailed, "Input could not be parsed as a TimeSpan."));
        }
    }
}

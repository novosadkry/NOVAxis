using System;
using System.Threading.Tasks;

using Discord.Commands;

namespace NOVAxis.TypeReaders
{
    public class TimeSpanTypeReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            try
            {
                string[] s = input.Split(':');
                int[] parts = new int[s.Length];

                for (int i = 0; i < s.Length; i++)
                {
                    if (int.TryParse(s[i], out int x))
                        parts[i] = x;
                    else
                        throw new Exception();
                }

                TimeSpan? timeSpan = s.Length switch
                {
                    1 => new TimeSpan(0, 0, 0, parts[0]),
                    2 => new TimeSpan(0, 0, parts[0], parts[1]),
                    3 => new TimeSpan(0, parts[0], parts[1], parts[2]),
                    _ => null
                };

                if (timeSpan != null)
                    return Task.FromResult(TypeReaderResult.FromSuccess(timeSpan));

                throw new Exception();
            }

            catch (Exception)
            {
                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Input could not be parsed as a TimeSpan."));
            }
        }
    }
}

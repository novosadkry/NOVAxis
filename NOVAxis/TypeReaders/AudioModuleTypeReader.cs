using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

namespace NOVAxis.TypeReaders
{
    class AudioModuleTypeReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            try
            {
                string[] s = input.Split(':');
                int[] parts = new int[s.Length];

                TimeSpan? timeSpan = null;

                for (int i = 0; i < s.Length; i++)
                {
                    if (int.TryParse(s[i], out int x))
                        parts[i] = x;

                    else
                        throw new Exception();
                }

                switch (s.Length)
                {
                    case 1:
                        timeSpan = new TimeSpan(0, 0, 0, parts[0]);
                        break;

                    case 2:
                        timeSpan = new TimeSpan(0, 0, parts[0], parts[1]);
                        break;

                    case 3:
                        timeSpan = new TimeSpan(0, parts[0], parts[1], parts[2]);
                        break;
                }

                if (timeSpan != null)
                    return Task.FromResult(TypeReaderResult.FromSuccess(timeSpan));

                else
                    throw new Exception();
            }

            catch (Exception)
            {
                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Input could not be parsed as a TimeSpan."));
            }
        }
    }
}

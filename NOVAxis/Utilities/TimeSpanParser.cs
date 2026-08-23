using System;

namespace NOVAxis.Utilities
{
    public static class TimeSpanParser
    {
        public static bool TryParse(string input, out TimeSpan result)
        {
            result = TimeSpan.Zero;

            if (string.IsNullOrEmpty(input))
                return false;

            var s = input.Split(':');

            if (s.Length is < 1 or > 3)
                return false;

            var parts = new int[s.Length];

            for (var i = 0; i < s.Length; i++)
            {
                if (!int.TryParse(s[i], out parts[i]))
                    return false;
            }

            try
            {
                result = s.Length switch
                {
                    1 => new TimeSpan(0, 0, 0, parts[0]),
                    2 => new TimeSpan(0, 0, parts[0], parts[1]),
                    _ => new TimeSpan(0, parts[0], parts[1], parts[2])
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            return true;
        }
    }
}

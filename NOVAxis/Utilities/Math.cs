namespace NOVAxis.Utilities
{
    public class Math
    {
        public static float SafeDivision(float a, float b)
        {
            return a == 0 ? 0 : a / b;
        }
    }
}

namespace NOVAxis.Services.WebServer
{
    public static class WebRoutes
    {
        public static class Api
        {
            public const string Base = "/api";

            public static class YoutubeDl
            {
                public const string GetDownload = "/download/{uuid}";
            }
        }
    }
}

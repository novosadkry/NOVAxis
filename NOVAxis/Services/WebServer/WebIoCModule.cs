using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

using EmbedIO;
using EmbedIO.Utilities;

namespace NOVAxis.Services.WebServer
{
    public class WebIoCModule : WebModuleBase
    {
        public static readonly object ScopeKey = new();

        private readonly IServiceProvider _serviceProvider;

        public WebIoCModule(IServiceProvider serviceProvider) : base(UrlPath.Root)
        {
            _serviceProvider = serviceProvider;
        }

        public override bool IsFinalHandler => false;

        protected override Task OnRequestAsync(IHttpContext context)
        {
            var scope = _serviceProvider.CreateScope();

            context.Items.Add(ScopeKey, scope);
            context.OnClose(OnContextClose);

            return Task.CompletedTask;
        }

        private static void OnContextClose(IHttpContext context)
        {
            var scope = context.Items[ScopeKey] as IServiceScope;
            context.Items.Remove(ScopeKey);
            scope!.Dispose();
        }
    }

    public static class WebServerExtensions
    {
        public static EmbedIO.WebServer WithIoC(this EmbedIO.WebServer webServer, IServiceProvider serviceProvider)
        {
            webServer.Modules.Add(new WebIoCModule(serviceProvider));
            return webServer;
        }
    }

    public static class HttpContextExtensions
    {
        public static IServiceScope GetIoCScope(this IHttpContext context) =>
            context.Items[WebIoCModule.ScopeKey] as IServiceScope
                ?? throw new ApplicationException("IoC scope not initialized for HTTP context");
    }
}

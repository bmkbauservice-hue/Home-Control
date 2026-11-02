using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace HomeControl.Api.Tests.Fakes;

// Der TestServer hat kein echtes Netzwerk, die Client-Adresse ist dort leer. Dieser Filter setzt sie
// vor allen anderen Middlewares, wie es sonst die echte Verbindung tut.
//
// Standard 127.0.0.1: So sieht es hinter "tailscale serve" aus. Mit dem Test-Header
// "X-Test-Remote-Ip" lässt sich eine andere Verbindungsadresse vorgeben (z. B. ein fremder Rechner).
public class FakeRemoteIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress =
                IPAddress.TryParse(context.Request.Headers[HeaderName], out var ip) ? ip : IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}

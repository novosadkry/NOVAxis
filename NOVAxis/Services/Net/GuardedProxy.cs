using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

namespace NOVAxis.Services.Net
{
    /// <summary>
    /// A proxy on loopback that yt-dlp is pointed at, which resolves every destination itself
    /// and refuses the ones leading back into the host's own network.
    ///
    /// Checking the address we were handed is not enough on its own. The name can answer
    /// differently the second time it is looked up, a public page can redirect to a private
    /// address, and an extractor fetches whatever the page tells it to. Every one of those
    /// arrives here as a fresh connection, and the socket is opened against the address this
    /// proxy resolved and approved rather than against a name resolved again later.
    /// </summary>
    public sealed class GuardedProxy : IHostedService, IAsyncDisposable
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(20);
        private const int MaxHeaderBytes = 32 * 1024;

        private readonly CancellationTokenSource _stopping = new();
        private readonly Lazy<string> _proxy;

        private TcpListener _listener;
        private Task _accepting;
        private string _credentials;

        private IOptions<AudioOptions> Options { get; }
        private ILogger<GuardedProxy> Logger { get; }

        public GuardedProxy(IOptions<AudioOptions> options, ILogger<GuardedProxy> logger)
        {
            Options = options;
            Logger = logger;

            // ExecutionAndPublication so a second caller waits for the first rather than
            // seeing a half started guard, and so a failure stays a failure: handing back
            // "no proxy needed" after one would let everything out unguarded
            _proxy = new Lazy<string>(Start, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// What to hand yt-dlp, or null when the guard is off and it connects directly.
        /// Brought up on first use rather than on a hosted service's turn to start, so that
        /// nothing can slip out unguarded while the boot order is still settling.
        /// </summary>
        public string ProxyUrl => _proxy.Value;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = _proxy.Value;
            return Task.CompletedTask;
        }

        private string Start()
        {
            var ytDlp = Options.Value.YtDlp;

            if (!ytDlp.RestrictNetwork)
            {
                Logger.Warning("Network restriction is off - yt-dlp may reach private addresses");
                return null;
            }

            // An operator routing yt-dlp through their own proxy has taken charge of where it
            // may reach, and quietly overriding them would be worse than standing aside
            if (ytDlp.ExtraArguments?.Any(a => a is "--proxy" or "--geo-verification-proxy") == true)
            {
                Logger.Warning("A proxy is set in 'Audio:YtDlp:ExtraArguments', so the " +
                               "network guard stands aside - egress is yours to restrict");

                return null;
            }

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
            }
            catch (Exception e)
            {
                // Refusing every fetch beats quietly dropping the guard
                throw new InvalidOperationException("The network guard could not open a port", e);
            }

            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // Loopback keeps it to this host; the token keeps it to this process
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"novaxis:{token}"));

            _accepting = Task.Run(() => AcceptAsync(_stopping.Token), CancellationToken.None);

            Logger.Info($"Network guard listening on 127.0.0.1:{port}");

            return $"http://novaxis:{token}@127.0.0.1:{port}";
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _stopping.CancelAsync();

            _listener?.Stop();

            if (_accepting != null)
            {
                try
                {
                    await _accepting.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (Exception) { /* going away regardless */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None);
            _stopping.Dispose();
        }

        private async Task AcceptAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Network guard stopped accepting: {e.Message}");
                    return;
                }

                _ = Task.Run(() => ServeAsync(client, cancellationToken), CancellationToken.None);
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;

                    await using var stream = client.GetStream();

                    using var headerSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    headerSource.CancelAfter(HeaderTimeout);

                    var request = await ReadHeadAsync(stream, headerSource.Token);

                    if (request == null)
                        return;

                    if (!Authorized(request))
                    {
                        await WriteAsync(stream, "HTTP/1.1 407 Proxy Authentication Required\r\n" +
                                                 "Proxy-Authenticate: Basic realm=\"novaxis\"\r\n" +
                                                 "Connection: close\r\n\r\n", cancellationToken);
                        return;
                    }

                    if (request.IsConnect)
                        await TunnelAsync(stream, request, cancellationToken);
                    else
                        await ForwardAsync(stream, request, cancellationToken);
                }
                catch (OperationCanceledException) { /* shutting down or timed out */ }
                catch (IOException) { /* the other end went away */ }
                catch (SocketException) { /* likewise */ }
                catch (Exception e)
                {
                    Logger.Debug($"Network guard failed to serve a request: {e.Message}");
                }
            }
        }

        private bool Authorized(ProxyRequest request)
        {
            var offered = request.Header("proxy-authorization");

            if (string.IsNullOrEmpty(offered) || !offered.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(offered[6..].Trim()),
                Encoding.UTF8.GetBytes(_credentials));
        }

        private async Task TunnelAsync(NetworkStream client, ProxyRequest request, CancellationToken cancellationToken)
        {
            var (host, port) = SplitAuthority(request.Target, 443);
            using var upstream = await ConnectAsync(host, port, cancellationToken);

            if (upstream == null)
            {
                await Refuse(client, host, cancellationToken);
                return;
            }

            await WriteAsync(client, "HTTP/1.1 200 Connection established\r\n\r\n", cancellationToken);

            await using var remote = upstream.GetStream();
            await PumpBothAsync(client, remote, cancellationToken);
        }

        private async Task ForwardAsync(NetworkStream client, ProxyRequest request, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(request.Target, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp)
            {
                await WriteAsync(client, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", cancellationToken);
                return;
            }

            using var upstream = await ConnectAsync(uri.DnsSafeHost, uri.Port, cancellationToken);

            if (upstream == null)
            {
                await Refuse(client, uri.DnsSafeHost, cancellationToken);
                return;
            }

            await using var remote = upstream.GetStream();

            var head = new StringBuilder()
                .Append(request.Method).Append(' ')
                .Append(uri.PathAndQuery).Append(' ')
                .Append("HTTP/1.1").Append("\r\n");

            foreach (var (name, value) in request.Headers)
            {
                // Hop by hop, and ours to decide rather than the client's to pass along
                if (name is "proxy-authorization" or "proxy-connection" or "connection" or "keep-alive")
                    continue;

                head.Append(value).Append("\r\n");
            }

            // One request per connection: after this the bytes are pumped blind, and reusing
            // the socket for a second host would send it somewhere never approved
            head.Append("Connection: close\r\n\r\n");

            await WriteAsync(remote, head.ToString(), cancellationToken);

            // Anything of the body already read while looking for the end of the headers
            if (request.Leftover.Length > 0)
            {
                await remote.WriteAsync(request.Leftover, cancellationToken);
                await remote.FlushAsync(cancellationToken);
            }

            await PumpBothAsync(client, remote, cancellationToken);
        }

        private async Task Refuse(NetworkStream client, string host, CancellationToken cancellationToken)
        {
            Logger.Warning($"Network guard refused a connection to '{host}'");

            await WriteAsync(client, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n", cancellationToken);
        }

        /// <summary>
        /// Opens a socket to an address this proxy resolved and approved. Connecting to the
        /// address rather than to the name is the point: a name looked up again here could
        /// answer differently than it did a moment ago.
        /// </summary>
        private async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            if (port is <= 0 or > 65535)
                return null;

            var addresses = await PrivateNetworks.ResolveAsync(host, cancellationToken);

            if (addresses.Count == 0)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);

            foreach (var address in addresses)
            {
                var client = new TcpClient(address.AddressFamily) { NoDelay = true };

                try
                {
                    await client.ConnectAsync(address, port, timeout.Token);
                    return client;
                }
                catch (Exception)
                {
                    client.Dispose();
                }
            }

            return null;
        }

        private static async Task PumpBothAsync(Stream a, Stream b, CancellationToken cancellationToken)
        {
            using var closing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var forward = PumpAsync(a, b, closing.Token);
            var back = PumpAsync(b, a, closing.Token);

            await Task.WhenAny(forward, back);

            // One direction ending means the exchange is over; the other is let go of
            await closing.CancelAsync();

            try
            {
                await Task.WhenAll(forward, back);
            }
            catch (Exception) { /* both ends are closing */ }
        }

        private static async Task PumpAsync(Stream from, Stream to, CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);

            try
            {
                int read;

                while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    await to.FlushAsync(cancellationToken);
                }
            }
            catch (Exception) { /* either side may hang up at any point */ }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async Task WriteAsync(Stream stream, string text, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static (string Host, int Port) SplitAuthority(string authority, int fallbackPort)
        {
            if (string.IsNullOrEmpty(authority))
                return (null, fallbackPort);

            // A bracketed IPv6 literal carries colons of its own
            if (authority.StartsWith('['))
            {
                var close = authority.IndexOf(']');

                if (close < 0)
                    return (null, fallbackPort);

                var literal = authority[1..close];
                var rest = authority[(close + 1)..];

                return rest.StartsWith(':') && int.TryParse(rest[1..], out var bracketed)
                    ? (literal, bracketed)
                    : (literal, fallbackPort);
            }

            var separator = authority.LastIndexOf(':');

            if (separator < 0)
                return (authority, fallbackPort);

            return int.TryParse(authority[(separator + 1)..], out var port)
                ? (authority[..separator], port)
                : (authority, fallbackPort);
        }

        private sealed class ProxyRequest
        {
            public string Method { get; init; }
            public string Target { get; init; }
            public List<(string Name, string Value)> Headers { get; init; }
            public byte[] Leftover { get; init; }

            public bool IsConnect => string.Equals(Method, "CONNECT", StringComparison.OrdinalIgnoreCase);

            public string Header(string name)
            {
                foreach (var (key, value) in Headers)
                {
                    if (key != name)
                        continue;

                    var colon = value.IndexOf(':');
                    return colon < 0 ? string.Empty : value[(colon + 1)..].Trim();
                }

                return null;
            }
        }

        private static async Task<ProxyRequest> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[MaxHeaderBytes];
            var filled = 0;
            var end = -1;

            while (filled < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken);

                if (read == 0)
                    break;

                filled += read;
                end = Find(buffer, filled);

                if (end >= 0)
                    break;
            }

            if (end < 0)
                return null;

            var text = Encoding.ASCII.GetString(buffer, 0, end);
            var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
                return null;

            var parts = lines[0].Split(' ', 3);

            if (parts.Length < 2)
                return null;

            var headers = new List<(string, string)>(lines.Length);

            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');

                if (colon > 0)
                    headers.Add((lines[i][..colon].Trim().ToLowerInvariant(), lines[i]));
            }

            var bodyStart = end + 4;

            return new ProxyRequest
            {
                Method = parts[0],
                Target = parts[1],
                Headers = headers,
                Leftover = filled > bodyStart ? buffer[bodyStart..filled] : []
            };
        }

        private static int Find(byte[] buffer, int length)
        {
            for (var i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' &&
                    buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    return i;
            }

            return -1;
        }
    }
}

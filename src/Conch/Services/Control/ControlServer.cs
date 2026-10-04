using System.Net.Sockets;
using System.Text;
using Conch.Utilities;

namespace Conch.Services.Control
{
    /// <summary>
    /// Listens on the shell's socket and answers one request per connection.
    /// </summary>
    /// <remarks>
    /// A connection is a request line in and a response line out. A request that waits --
    /// <c>run --wait</c> -- simply holds its connection open until it has an answer, so there
    /// is no polling and no second channel.
    /// </remarks>
    public sealed class ControlServer : IDisposable
    {
        private const string LogCategory = "Control";

        private readonly string _path;
        private readonly Func<ControlRequest, Task<ControlResponse>> _handle;
        private readonly CancellationTokenSource _stop = new();
        private Socket? _listener;

        /// <param name="path">The socket file to listen on.</param>
        /// <param name="handle">Answers a request; called off the UI thread.</param>
        public ControlServer(string path, Func<ControlRequest, Task<ControlResponse>> handle)
        {
            _path = path;
            _handle = handle;
        }

        public string Path => _path;

        public void Start()
        {
            ControlEndpoint.EnsureDirectory(System.IO.Path.GetDirectoryName(_path));

            // A file left by an earlier process with the same id would make Bind fail.
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_path));
            _listener.Listen(16);

            _ = AcceptLoopAsync(_listener, _stop.Token);
            Log.Info(LogCategory, $"Listening on {_path}.");
        }

        private async Task AcceptLoopAsync(Socket listener, CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                Socket client;
                try
                {
                    client = await listener.AcceptAsync(cancel).ConfigureAwait(false);
                }
                catch (Exception) when (cancel.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(LogCategory, $"Accept failed: {ex.Message}");
                    continue;
                }

                _ = ServeAsync(client);
            }
        }

        private async Task ServeAsync(Socket client)
        {
            using var _ = client;
            await using var stream = new NetworkStream(client, ownsSocket: false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            ControlResponse response;
            try
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                var request = line == null ? null : ControlJson.Read<ControlRequest>(line);
                response = request == null
                    ? ControlResponse.Fail(ControlExit.Usage, "Empty request.")
                    : await _handle(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, "Request failed", ex);
                response = ControlResponse.Fail(ControlExit.Failed, ex.Message);
            }

            try
            {
                await writer.WriteLineAsync(ControlJson.Write(response)).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The caller gave up -- Ctrl+C on a `conch run --wait`. Nothing to tell it.
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener?.Dispose();
            try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

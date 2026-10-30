using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // A fake transport exercises the same parser, identity checks and effect verification.
    internal interface IHyprlandTransport
    {
        string SessionId { get; }
        Task<string> RequestAsync(string command, CancellationToken cancellation);
        IAsyncEnumerable<string> Events(CancellationToken cancellation);
    }

    internal sealed class HyprlandIpc : IHyprlandTransport
    {
        private readonly string directory;
        public string SessionId { get; }

        internal HyprlandIpc(string runtimeDirectory, string signature)
        {
            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Path.IsPathFullyQualified(runtimeDirectory)
                || string.IsNullOrWhiteSpace(signature) || signature != Path.GetFileName(signature)
                || signature == "." || signature == "..")
                throw new InvalidOperationException("Hyprland session socket location is unavailable.");
            SessionId = signature;
            directory = Path.Combine(runtimeDirectory, "hypr", signature);
        }

        private void CheckSession()
        {
            if (Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") != SessionId)
                throw new InvalidOperationException("Hyprland session changed; restart the desktop connection.");
        }

        public async Task<string> RequestAsync(string command, CancellationToken cancellation)
        {
            CheckSession();
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            using Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(directory, ".socket.sock")), deadline.Token).ConfigureAwait(false);
            using NetworkStream stream = new NetworkStream(socket, false);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(command), deadline.Token).ConfigureAwait(false);
            using MemoryStream response = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (response.Length + count > 4 * 1024 * 1024) throw new IOException("Hyprland response exceeded 4 MiB.");
                response.Write(buffer, 0, count);
            }
            CheckSession();
            return Encoding.UTF8.GetString(response.GetBuffer(), 0, checked((int)response.Length));
        }

        public async IAsyncEnumerable<string> Events([EnumeratorCancellation] CancellationToken cancellation)
        {
            CheckSession();
            using Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(directory, ".socket2.sock")), deadline.Token).ConfigureAwait(false);
            }
            using NetworkStream stream = new NetworkStream(socket, false);
            byte[] buffer = new byte[4096];
            StringBuilder line = new StringBuilder();
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            {
                // Event names and geometry are ASCII; titles are intentionally not consumed.
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] == '\n') { yield return line.ToString(); line.Clear(); }
                    else if (line.Length < 65536) line.Append((char)buffer[i]);
                    else throw new IOException("Hyprland event exceeded its bound.");
                }
            }
            throw new IOException("Hyprland event socket disconnected.");
        }
    }
}

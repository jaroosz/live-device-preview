using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LiveDevicePreview.Editor
{
    internal sealed class ControlServer : IDisposable
    {
        private const int HeartbeatTimeoutSeconds = 15;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, ConnectedPhone> _connectedPhones =
            new Dictionary<string, ConnectedPhone>(StringComparer.Ordinal);
        private readonly object _connectedPhonesLock = new object();

        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptTask;
        private string _hostId;

        private sealed class ConnectedPhone
        {
            public PhoneInfo Info;
            public TcpClient Client;
        }

        public event Action<PhoneInfo, bool> PhoneConnectionChanged;

        public bool IsRunning => _listener != null;
        public int Port { get; private set; }

        public IReadOnlyList<PhoneInfo> ConnectedPhones
        {
            get
            {
                lock (_connectedPhonesLock)
                {
                    var phones = new List<PhoneInfo>(_connectedPhones.Count);
                    foreach (var connection in _connectedPhones.Values)
                        phones.Add(connection.Info);
                    return phones;
                }
            }
        }

        public void Start(int port, string hostId)
        {
            if (IsRunning)
            {
                if (Port == port)
                    return;

                throw new InvalidOperationException($"Live Device Preview control server is already running on port {Port}.");
            }

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start(8);

            _hostId = hostId;
            Port = port;
            _listener = listener;
            _cancellation = new CancellationTokenSource();
            EditorApplication.update += DispatchMainThreadActions;
            _acceptTask = AcceptLoopAsync(listener, _cancellation.Token);
        }

        public void Stop()
        {
            var listener = _listener;
            if (listener == null)
                return;

            _listener = null;
            EditorApplication.update -= DispatchMainThreadActions;
            _cancellation.Cancel();
            listener.Stop();

            List<ConnectedPhone> connections;
            lock (_connectedPhonesLock)
            {
                connections = new List<ConnectedPhone>(_connectedPhones.Values);
                _connectedPhones.Clear();
            }

            foreach (var connection in connections)
            {
                connection.Client.Close();
                EnqueueConnectionChanged(connection.Info, false);
            }

            DispatchMainThreadActions();
            _cancellation.Dispose();
            _cancellation = null;
            _acceptTask = null;
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    _ = HandleClientAsync(client, cancellationToken);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    client?.Close();
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellation)
        {
            PhoneInfo phone = null;
            var registered = false;

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation))
                    {
                        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var json = await ReadMessageAsync(stream, handshakeTimeout.Token);
                        if (json == null)
                            return;

                        var message = JsonUtility.FromJson<ControlMessage>(json);
                        if (message == null || message.type != ControlProtocol.HelloType)
                        {
                            await SendRejectionAsync(stream, "Expected hello message.", serverCancellation);
                            return;
                        }

                        var hello = JsonUtility.FromJson<HelloMessage>(json);
                        var rejectionReason = ValidateHello(hello);
                        if (rejectionReason != null)
                        {
                            await SendRejectionAsync(stream, rejectionReason, serverCancellation);
                            return;
                        }

                        phone = new PhoneInfo
                        {
                            DeviceId = hello.deviceId.Trim(),
                            DeviceName = hello.deviceName.Trim(),
                            Model = hello.model?.Trim() ?? string.Empty,
                            Width = hello.width,
                            Height = hello.height,
                            Codecs = hello.codecs ?? Array.Empty<string>(),
                            SupportedSensors = hello.supportedSensors
                        };

                        lock (_connectedPhonesLock)
                        {
                            if (_connectedPhones.ContainsKey(phone.DeviceId))
                            {
                                rejectionReason = "This phone is already connected.";
                            }
                            else
                            {
                                _connectedPhones.Add(phone.DeviceId, new ConnectedPhone { Info = phone, Client = client });
                                registered = true;
                            }
                        }

                        if (rejectionReason != null)
                        {
                            await SendRejectionAsync(stream, rejectionReason, serverCancellation);
                            return;
                        }

                        var welcome = new HandshakeResponse
                        {
                            type = ControlProtocol.WelcomeType,
                            protocolVersion = ControlProtocol.Version,
                            accepted = true,
                            reason = string.Empty,
                            hostId = _hostId,
                            hostName = Discovery.HostName,
                            udpPort = UnityEditor.EditorPrefs.GetInt("LiveDevicePreview.UdpPort", 7778)
                        };
                        await WriteMessageAsync(stream, JsonUtility.ToJson(welcome), serverCancellation);
                        EnqueueConnectionChanged(phone, true);

                        await ReadClientMessagesAsync(stream, serverCancellation);
                    }
                }
            }
            catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Live Device Preview: Phone handshake or heartbeat timed out.");
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"Live Device Preview: Control connection ended: {exception.Message}");
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"Live Device Preview: Control connection failed: {exception.Message}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Live Device Preview: Failed processing a control connection: {exception.Message}");
            }
            finally
            {
                client.Close();
                if (registered && phone != null)
                {
                    var removed = false;
                    lock (_connectedPhonesLock)
                    {
                        if (_connectedPhones.TryGetValue(phone.DeviceId, out var current) &&
                            ReferenceEquals(current.Client, client))
                            removed = _connectedPhones.Remove(phone.DeviceId);
                    }

                    if (removed)
                        EnqueueConnectionChanged(phone, false);
                }
            }
        }

        private async Task ReadClientMessagesAsync(NetworkStream stream, CancellationToken serverCancellation)
        {
            while (!serverCancellation.IsCancellationRequested)
            {
                using (var heartbeatTimeout = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation))
                {
                    heartbeatTimeout.CancelAfter(TimeSpan.FromSeconds(HeartbeatTimeoutSeconds));
                    var json = await ReadMessageAsync(stream, heartbeatTimeout.Token);
                    if (json == null)
                        return;

                    var message = JsonUtility.FromJson<ControlMessage>(json);
                    if (message == null || message.type != ControlProtocol.HeartbeatType)
                        throw new InvalidOperationException("Unsupported control message after handshake.");
                    if (message.protocolVersion != ControlProtocol.Version)
                        throw new InvalidOperationException("Phone uses an incompatible protocol version.");

                    await WriteMessageAsync(stream, json, heartbeatTimeout.Token);
                }
            }
        }

        private static string ValidateHello(HelloMessage hello)
        {
            if (hello == null)
                return "Invalid hello message.";
            if (hello.protocolVersion != ControlProtocol.Version)
                return $"Unsupported protocol version {hello.protocolVersion}.";
            if (string.IsNullOrWhiteSpace(hello.deviceId) || hello.deviceId.Length > 128)
                return "Phone device ID is missing or too long.";
            if (string.IsNullOrWhiteSpace(hello.deviceName) || hello.deviceName.Length > 128)
                return "Phone device name is missing or too long.";
            if (hello.model != null && hello.model.Length > 128)
                return "Phone model name is too long.";
            if (hello.width < 1 || hello.width > 16384 || hello.height < 1 || hello.height > 16384)
                return "Phone screen dimensions are outside the supported range.";
            if (hello.codecs != null && hello.codecs.Length > 16)
                return "Phone sent too many codec entries.";

            return null;
        }

        private async Task SendRejectionAsync(NetworkStream stream, string reason, CancellationToken cancellationToken)
        {
            var response = new HandshakeResponse
            {
                type = ControlProtocol.WelcomeType,
                protocolVersion = ControlProtocol.Version,
                accepted = false,
                reason = reason,
                hostId = _hostId,
                hostName = Discovery.HostName
            };
            await WriteMessageAsync(stream, JsonUtility.ToJson(response), cancellationToken);
        }

        private static async Task<string> ReadMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var header = new byte[4];
            if (!await ReadExactlyAsync(stream, header, 0, header.Length, cancellationToken))
                return null;

            var messageLength =
                (header[0] << 24) |
                (header[1] << 16) |
                (header[2] << 8) |
                header[3];

            if (messageLength < 1 || messageLength > ControlProtocol.MaximumMessageSize)
                throw new InvalidDataException($"Control message length {messageLength} is invalid.");

            var payload = new byte[messageLength];
            if (!await ReadExactlyAsync(stream, payload, 0, payload.Length, cancellationToken))
                throw new EndOfStreamException("Control connection closed before the full message arrived.");

            return Encoding.UTF8.GetString(payload);
        }

        private static async Task WriteMessageAsync(NetworkStream stream, string json, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            if (payload.Length > ControlProtocol.MaximumMessageSize)
                throw new InvalidDataException("Control message exceeds the maximum allowed size.");

            var frame = new byte[payload.Length + 4];
            frame[0] = (byte)(payload.Length >> 24);
            frame[1] = (byte)(payload.Length >> 16);
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            await stream.WriteAsync(frame, 0, frame.Length, cancellationToken);
        }

        private static async Task<bool> ReadExactlyAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
                if (read == 0)
                {
                    if (totalRead == 0)
                        return false;

                    throw new EndOfStreamException("Control connection closed in the middle of a message.");
                }

                totalRead += read;
            }

            return true;
        }

        private void EnqueueConnectionChanged(PhoneInfo phone, bool connected)
        {
            _mainThreadActions.Enqueue(() => PhoneConnectionChanged?.Invoke(phone, connected));
        }

        private void DispatchMainThreadActions()
        {
            while (_mainThreadActions.TryDequeue(out var action))
                action();
        }
    }
}

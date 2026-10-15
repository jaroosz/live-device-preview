using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class MobiViewDiscovery
{
    private const string VisibilityPreferenceKey = "MobiView.Visible";
    private const string ServiceType = "_mobiview._tcp.local.";
    private const int DiscoveryPort = 5353;
    private const double AnnouncementInterval = 5.0;

    private static int ControlPort => EditorPrefs.GetInt("MobiView.TcpPort", 7777);

    private sealed class InterfaceSocket
    {
        public IPAddress Address;
        public Socket Socket;
    }

    private static readonly List<InterfaceSocket> Sockets = new List<InterfaceSocket>();
    private static readonly string DeviceId = GetDeviceId();
    private static string MachineLabel => GetMachineLabel();

    public static string HostName
    {
        get
        {
            string name = EditorPrefs.GetString("MobiView.HostName", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                name = Environment.MachineName;
            return name;
        }
    }

    private static MobiViewControlServer _controlServer;
    private static double _nextAnnouncementTime;
    private static bool _enabled;

    public static event Action<MobiViewPhoneInfo, bool> PhoneConnectionChanged;

    public static IReadOnlyList<MobiViewPhoneInfo> ConnectedPhones =>
        _controlServer?.ConnectedPhones ?? Array.Empty<MobiViewPhoneInfo>();

    static MobiViewDiscovery()
    {
        EditorApplication.update += Update;
        EditorApplication.quitting += Stop;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
    }

    public static bool SetVisible(bool visible)
    {
        if (visible)
        {
            try
            {
                Start();
                EditorPrefs.SetBool(VisibilityPreferenceKey, true);
                return true;
            }
            catch (Exception exception)
            {
                Stop();
                EditorPrefs.SetBool(VisibilityPreferenceKey, false);
                Debug.LogError($"MobiView: Could not make this computer visible: {exception.Message}");
                return false;
            }
        }
        else
        {
            Stop();
            EditorPrefs.SetBool(VisibilityPreferenceKey, false);
            return true;
        }
    }

    private static void Start()
    {
        if (_enabled)
            return;

        _controlServer = new MobiViewControlServer();
        _controlServer.PhoneConnectionChanged += OnPhoneConnectionChanged;
        _controlServer.Start(ControlPort, DeviceId);
        _enabled = true;
        _nextAnnouncementTime = 0;
        SendAnnouncements();
    }

    private static void Stop()
    {
        if (!_enabled && Sockets.Count == 0 && _controlServer == null)
            return;

        _enabled = false;
        _controlServer?.Stop();
        if (_controlServer != null)
            _controlServer.PhoneConnectionChanged -= OnPhoneConnectionChanged;
        _controlServer = null;

        foreach (var entry in Sockets)
        {
            try
            {
                var goodbye = BuildAnnouncement(entry.Address, 0);
                for (var i = 0; i < 3; i++)
                    entry.Socket.SendTo(goodbye, new IPEndPoint(IPAddress.Parse("224.0.0.251"), DiscoveryPort));
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"MobiView: Could not send mDNS goodbye on {entry.Address}: {exception.Message}");
            }
            finally
            {
                entry.Socket.Dispose();
            }
        }

        Sockets.Clear();
    }

    private static void OnPhoneConnectionChanged(MobiViewPhoneInfo phone, bool connected)
    {
        PhoneConnectionChanged?.Invoke(phone, connected);
    }

    private static void Update()
    {
        if (!_enabled || EditorApplication.timeSinceStartup < _nextAnnouncementTime)
            return;

        SendAnnouncements();
    }

    private static void SendAnnouncements()
    {
        EnsureInterfaceSockets();

        foreach (var entry in Sockets)
        {
            try
            {
                var packet = BuildAnnouncement(entry.Address);
                entry.Socket.SendTo(packet, new IPEndPoint(IPAddress.Parse("224.0.0.251"), DiscoveryPort));
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"MobiView: Could not announce discovery on {entry.Address}: {exception.Message}");
            }
        }

        _nextAnnouncementTime = EditorApplication.timeSinceStartup + AnnouncementInterval;
    }

    private static void EnsureInterfaceSockets()
    {
        var addresses = GetLocalIPv4Addresses();
        var desiredAddresses = new HashSet<string>();
        foreach (var address in addresses)
            desiredAddresses.Add(address.ToString());

        for (var i = Sockets.Count - 1; i >= 0; i--)
        {
            var entry = Sockets[i];
            if (desiredAddresses.Contains(entry.Address.ToString()))
                continue;

            try
            {
                entry.Socket.SendTo(
                    BuildAnnouncement(entry.Address, 0),
                    new IPEndPoint(IPAddress.Parse("224.0.0.251"), DiscoveryPort));
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"MobiView: Could not withdraw mDNS on {entry.Address}: {exception.Message}");
            }

            entry.Socket.Dispose();
            Sockets.RemoveAt(i);
        }

        foreach (var address in addresses)
        {
            var alreadyOpen = false;
            foreach (var entry in Sockets)
            {
                if (entry.Address.Equals(address))
                {
                    alreadyOpen = true;
                    break;
                }
            }

            if (alreadyOpen)
                continue;

            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.ExclusiveAddressUse = false;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(address, DiscoveryPort));
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
                Sockets.Add(new InterfaceSocket { Address = address, Socket = socket });
            }
            catch (SocketException exception)
            {
                socket?.Dispose();
                Debug.LogWarning($"MobiView: Could not enable mDNS on {address}: {exception.Message}");
            }
        }
    }

    private static List<IPAddress> GetLocalIPv4Addresses()
    {
        var addresses = new List<IPAddress>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var address = unicastAddress.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !addresses.Contains(address))
                    addresses.Add(address);
            }
        }

        return addresses;
    }

    private static byte[] BuildAnnouncement(IPAddress address, uint ttl = 120)
    {
        var instanceName = HostName.Replace(".", "");
        if (string.IsNullOrWhiteSpace(instanceName)) instanceName = "MobiView";
        var serviceName = $"{instanceName}.{ServiceType}";
        var hostName = $"{MachineLabel}.local.";
        var packet = new List<byte>(256);

        WriteUInt16(packet, 0);
        WriteUInt16(packet, 0x8400);
        WriteUInt16(packet, 0);
        WriteUInt16(packet, 2);
        WriteUInt16(packet, 0);
        WriteUInt16(packet, 3);

        var metaRecord = new List<byte>();
        WriteName(metaRecord, ServiceType);
        WriteResource(packet, "_services._dns-sd._udp.local.", 12, 1, ttl, metaRecord);

        var pointerRecord = new List<byte>();
        WriteName(pointerRecord, serviceName);
        WriteResource(packet, ServiceType, 12, 1, ttl, pointerRecord);

        var textRecord = new List<byte>();
        WriteText(textRecord, $"v=1");
        WriteText(textRecord, $"id={DeviceId}");
        WriteText(textRecord, $"name={HostName}");
        WriteText(textRecord, $"ip={address.ToString()}");
        WriteResource(packet, serviceName, 16, 0x8001, ttl, textRecord);

        var serviceRecord = new List<byte>();
        WriteUInt16(serviceRecord, 0);
        WriteUInt16(serviceRecord, 0);
        WriteUInt16(serviceRecord, ControlPort);
        WriteName(serviceRecord, hostName);
        WriteResource(packet, serviceName, 33, 0x8001, ttl, serviceRecord);

        WriteResource(packet, hostName, 1, 0x8001, ttl, new List<byte>(address.GetAddressBytes()));

        return packet.ToArray();
    }

    private static void WriteResource(List<byte> packet, string name, ushort type, ushort recordClass, uint ttl, List<byte> data)
    {
        WriteName(packet, name);
        WriteUInt16(packet, type);
        WriteUInt16(packet, recordClass);
        WriteUInt32(packet, ttl);
        WriteUInt16(packet, (ushort)data.Count);
        packet.AddRange(data);
    }

    private static void WriteName(List<byte> packet, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }

        packet.Add(0);
    }

    private static void WriteText(List<byte> packet, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        packet.Add((byte)bytes.Length);
        packet.AddRange(bytes);
    }

    private static void WriteUInt16(List<byte> packet, int value)
    {
        packet.Add((byte)(value >> 8));
        packet.Add((byte)value);
    }

    private static void WriteUInt32(List<byte> packet, uint value)
    {
        packet.Add((byte)(value >> 24));
        packet.Add((byte)(value >> 16));
        packet.Add((byte)(value >> 8));
        packet.Add((byte)value);
    }

    private static string GetDeviceId()
    {
        const string key = "MobiView.DeviceId";
        var deviceId = EditorPrefs.GetString(key, string.Empty);
        if (!string.IsNullOrEmpty(deviceId))
            return deviceId;

        deviceId = Guid.NewGuid().ToString("N");
        EditorPrefs.SetString(key, deviceId);
        return deviceId;
    }

    private static string GetMachineLabel()
    {
        var builder = new StringBuilder();
        foreach (var character in HostName.ToLowerInvariant())
        {
            if ((character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '-')
                builder.Append(character);
            else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                builder.Append('-');
        }

        var label = builder.ToString().Trim('-');
        if (label.Length == 0)
            label = "mobiview-host";
        if (label.Length > 50)
            label = label.Substring(0, 50).TrimEnd('-');

        return label;
    }
}

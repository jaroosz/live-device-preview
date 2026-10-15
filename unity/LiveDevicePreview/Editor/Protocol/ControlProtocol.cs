using System;

namespace LiveDevicePreview.Editor
{
    [Serializable]
    internal class ControlMessage
    {
        public string type;
        public int protocolVersion;
    }

    [Serializable]
    internal sealed class HelloMessage : ControlMessage
    {
        public string deviceId;
        public string deviceName;
        public string model;
        public int width;
        public int height;
        public string[] codecs;
        public int supportedSensors;
    }

    [Serializable]
    internal sealed class HandshakeResponse : ControlMessage
    {
        public bool accepted;
        public string reason;
        public string hostId;
        public string hostName;
        public int udpPort;
    }

    [Serializable]
    internal sealed class PhoneInfo
    {
        public string DeviceId;
        public string DeviceName;
        public string Model;
        public int Width;
        public int Height;
        public string[] Codecs;
        public int SupportedSensors;
    }

    internal static class ControlProtocol
    {
        public const int Version = 1;
        public const int MaximumMessageSize = 16 * 1024;
        public const string HelloType = "hello";
        public const string WelcomeType = "welcome";
        public const string HeartbeatType = "heartbeat";
    }
}

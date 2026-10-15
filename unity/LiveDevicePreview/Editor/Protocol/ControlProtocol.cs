using System;

[Serializable]
internal class MobiViewControlMessage
{
    public string type;
    public int protocolVersion;
}

[Serializable]
internal sealed class MobiViewHelloMessage : MobiViewControlMessage
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
internal sealed class MobiViewHandshakeResponse : MobiViewControlMessage
{
    public bool accepted;
    public string reason;
    public string hostId;
    public string hostName;
    public int udpPort;
}

[Serializable]
internal sealed class MobiViewPhoneInfo
{
    public string DeviceId;
    public string DeviceName;
    public string Model;
    public int Width;
    public int Height;
    public string[] Codecs;
    public int SupportedSensors;
}

internal static class MobiViewControlProtocol
{
    public const int Version = 1;
    public const int MaximumMessageSize = 16 * 1024;
    public const string HelloType = "hello";
    public const string WelcomeType = "welcome";
    public const string HeartbeatType = "heartbeat";
}

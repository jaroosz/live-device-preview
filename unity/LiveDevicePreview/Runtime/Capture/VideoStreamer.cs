using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LiveDevicePreview.Nvenc;
using UnityEngine;

namespace LiveDevicePreview
{
    public static class VideoStreamer
    {
        private static UdpClient _udpServer;
        private static CancellationTokenSource _cts;
        private static IPEndPoint _clientEndpoint;
        private static NvencEncoder _encoder;

        private static int _sequenceNumber;
        private static long _framesEnqueued;

        private const int MaxUdpPayload = 1400;
        private static int _bitrate = 15_000_000;
        private const int HeaderSize = 31;

        public static ushort RequiredInputMask { get; set; }
        public static ushort ClientSupportedSensors { get; set; }

        public static bool IsRunning { get; private set; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceDelegate(IntPtr thisPtr, out IntPtr ppDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr thisPtr);

        // new_tex
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2DDelegate(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(IntPtr device, out IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResourceDelegate(IntPtr context, IntPtr dst, IntPtr src);

        private static IntPtr _nvencTexture;
        private static IntPtr _d3dContext;
        private static CopyResourceDelegate _copyResource;

        private static bool CreateNvencTexture(IntPtr device, int width, int height)
        {
            try
            {
                IntPtr vtable = Marshal.ReadIntPtr(device);

                var createTex = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                    Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size));   // ID3D11Device::CreateTexture2D

                var desc = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = 87,          // DXGI_FORMAT_B8G8R8A8_UNORM
                    SampleCount = 1,
                    SampleQuality = 0,
                    Usage = 0,            // D3D11_USAGE_DEFAULT
                    BindFlags = 0x20,     // D3D11_BIND_RENDER_TARGET
                    CPUAccessFlags = 0,
                    MiscFlags = 0
                };

                int hr = createTex(device, ref desc, IntPtr.Zero, out _nvencTexture);
                if (hr < 0 || _nvencTexture == IntPtr.Zero)
                {
                    UnityEngine.Debug.LogError($"Live Device Preview: CreateTexture2D failed, HRESULT=0x{hr:X8}");
                    return false;
                }

                var getCtx = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(
                    Marshal.ReadIntPtr(vtable, 40 * IntPtr.Size));  // ID3D11Device::GetImmediateContext
                getCtx(device, out _d3dContext);

                IntPtr ctxVtable = Marshal.ReadIntPtr(_d3dContext);
                _copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                    Marshal.ReadIntPtr(ctxVtable, 47 * IntPtr.Size)); // ID3D11DeviceContext::CopyResource

                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Live Device Preview: CreateNvencTexture failed: {ex.Message}");
                return false;
            }
        }

        private static void ReleaseCom(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            var release = (ReleaseDelegate)Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(vtable, 2 * IntPtr.Size), typeof(ReleaseDelegate));
            release(obj);
            obj = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_TEXTURE2D_DESC
        {
            public uint Width, Height, MipLevels, ArraySize, Format;
            public uint SampleCount, SampleQuality;
            public uint Usage, BindFlags, CPUAccessFlags, MiscFlags;
        }

        public static void Start(int port, int bitrate = 15_000_000)
        {
            if (IsRunning) return;

            _bitrate = bitrate;
            _cts = new CancellationTokenSource();
            _udpServer = new UdpClient(port)
            {
                DontFragment = true
            };
            _udpServer.Client.SendBufferSize = 2 * 1024 * 1024;
            _udpServer.Client.ReceiveBufferSize = 256 * 1024;
            _clientEndpoint = null;
            _sequenceNumber = 0;
            _framesEnqueued = 0;
            _encoder = null;

            Task.Run(() => ListenForClients(_cts.Token));

            FrameCapture.OnFrameCaptured += HandleFrame;
            IsRunning = true;
        
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                UnityEngine.InputSystem.InputSystem.DisableDevice(UnityEngine.InputSystem.Mouse.current);
            }
        }

        public static void Stop()
        {
            if (!IsRunning) return;
            FrameCapture.OnFrameCaptured -= HandleFrame;

            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                UnityEngine.InputSystem.InputSystem.EnableDevice(UnityEngine.InputSystem.Mouse.current);
            }

            _cts?.Cancel();
            _udpServer?.Close();
            _encoder?.Dispose();

            ReleaseCom(ref _nvencTexture);
            ReleaseCom(ref _d3dContext);
            _copyResource = null;

            _cts = null;
            _udpServer = null;
            _clientEndpoint = null;
            _encoder = null;

            IsRunning = false;
        }

        private static void HandleFrame(IntPtr texturePtr, int width, int height)
        {
            if (_clientEndpoint == null)
            {
                // Omit spamming if not connected, but it means capture IS running if it hits here
                return;
            }

            if (texturePtr == IntPtr.Zero) return;

            try
            {
                if (_encoder == null)
                {
                    var graphicsType = UnityEngine.SystemInfo.graphicsDeviceType;

                    if (graphicsType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
                    {
                        UnityEngine.Debug.LogError($"Live Device Preview: Unsupported Graphics API ({graphicsType})! Please switch Unity to Direct3D11.");
                        return;
                    }

                    IntPtr devicePtr = GetD3D11Device(texturePtr);
                    if (devicePtr == IntPtr.Zero)
                    {
                        UnityEngine.Debug.LogError("Live Device Preview: D3D11 device pointer is null!");
                        return;
                    }
                    if (!CreateNvencTexture(devicePtr, width, height)) return;
                    _encoder = new NvencEncoder(devicePtr, width, height, VideoCodec.H265, _bitrate, FrameCapture.TargetFps);
                }

                _copyResource(_d3dContext, _nvencTexture, texturePtr);
                var result = _encoder.EncodeTexture(_nvencTexture);
                if (result == null) return;

                var (buffer, length, frameNumber) = result.Value;
                try
                {
                    EnqueueFrame(buffer, length, width, height, frameNumber);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Live Device Preview: Encode/Send error: {ex.Message}");
            }
        }

        private static void EnqueueFrame(byte[] h265Data, int length, int width, int height, uint frameNumber)
        {
            var currentFrame = Interlocked.Increment(ref _framesEnqueued);

            var totalFragments = (ushort)Math.Ceiling((double)length / MaxUdpPayload);
            var seq = (uint)(Interlocked.Increment(ref _sequenceNumber) - 1);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            for (ushort i = 0; i < totalFragments; i++)
            {
                var offset = i * MaxUdpPayload;
                var payloadSize = Math.Min(MaxUdpPayload, length - offset);

                var packetSize = HeaderSize + payloadSize;
                var rented = ArrayPool<byte>.Shared.Rent(packetSize);

                try
                {
                    var span = rented.AsSpan(0, packetSize);
                    int o = 0;

                    span[o++] = 1; // PacketType.VideoFrame
                    BitConverter.TryWriteBytes(span.Slice(o, 4), seq); o += 4;
                    BitConverter.TryWriteBytes(span.Slice(o, 8), timestamp); o += 8;
                    BitConverter.TryWriteBytes(span.Slice(o, 4), width); o += 4;
                    BitConverter.TryWriteBytes(span.Slice(o, 4), height); o += 4;
                    BitConverter.TryWriteBytes(span.Slice(o, 2), i); o += 2;
                    BitConverter.TryWriteBytes(span.Slice(o, 2), totalFragments); o += 2;
                    BitConverter.TryWriteBytes(span.Slice(o, 4), frameNumber); o += 4;
                    BitConverter.TryWriteBytes(span.Slice(o, 2), RequiredInputMask); o += 2;

                    h265Data.AsSpan(offset, payloadSize).CopyTo(span.Slice(HeaderSize));

                    _udpServer?.Send(rented, packetSize, _clientEndpoint);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        private static async Task ListenForClients(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _udpServer != null)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync();
                    if (result.Buffer.Length > 0 && result.Buffer[0] == 0) // PacketType.ConnectionRequest
                    {
                        _clientEndpoint = result.RemoteEndPoint;
                        _encoder?.ForceNextIDR();

                        var response = new byte[9];
                        response[0] = 2; // PacketType.ConnectionResponse
                        BitConverter.TryWriteBytes(response.AsSpan(1), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        await _udpServer.SendAsync(response, response.Length, _clientEndpoint);
                    }
                    else if (result.Buffer.Length > 0 && result.Buffer[0] == 3) // PacketType.InputFrame
                    {
                        InputReceiver.EnqueuePacket(result.Buffer);
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        UnityEngine.Debug.LogWarning($"Live Device Preview: UDP Listen Error: {ex.Message}");
                }
            }
        }

        private static IntPtr GetD3D11Device(IntPtr texturePtr)
        {
            try
            {
                IntPtr vtable = Marshal.ReadIntPtr(texturePtr);
                IntPtr getDevicePtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var getDevice = (GetDeviceDelegate)Marshal.GetDelegateForFunctionPointer(getDevicePtr, typeof(GetDeviceDelegate));

                IntPtr device = IntPtr.Zero;
                getDevice(texturePtr, out device);

                if (device != IntPtr.Zero)
                {
                    IntPtr deviceVtable = Marshal.ReadIntPtr(device);
                    IntPtr releasePtr = Marshal.ReadIntPtr(deviceVtable, 2 * IntPtr.Size);
                    var release = (ReleaseDelegate)Marshal.GetDelegateForFunctionPointer(releasePtr, typeof(ReleaseDelegate));
                    release(device);
                }

                return device;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Live Device Preview: Failed to extract D3D11 device pointer: {ex.Message}");
                return IntPtr.Zero;
            }
        }
    }
}


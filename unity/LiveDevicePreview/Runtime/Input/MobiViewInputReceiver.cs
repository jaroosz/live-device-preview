using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class MobiViewInputReceiver
{
    private static uint _lastFrameId;
    private static Touchscreen _virtualTouch;
    private static readonly ConcurrentQueue<byte[]> _queue = new ConcurrentQueue<byte[]>();
    private static readonly HashSet<int> _activeTouches = new HashSet<int>();

    // debug
    internal struct TouchDebugInfo
    {
        public Vector2 Position;
        public int Raw;
        public UnityEngine.InputSystem.TouchPhase Phase;
        public float Time;
    }

    internal static readonly Dictionary<int, TouchDebugInfo> DebugTouches = new Dictionary<int, TouchDebugInfo>();
    internal static Touchscreen VirtualTouch => _virtualTouch;
    // debug


    private static Accelerometer _virtualAccel;
    private static UnityEngine.InputSystem.Gyroscope _virtualGyro;
    private static GravitySensor _virtualGravity;
    private static AttitudeSensor _virtualAttitude;
    private static LinearAccelerationSensor _virtualLinearAccel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        _lastFrameId = 0;
        DebugTouches.Clear(); // debug
        _activeTouches.Clear();

        // Remove old virtual devices from previous Play Mode sessions
        var devicesToRemove = new List<InputDevice>();
        foreach (var device in InputSystem.devices)
        {
            if (device != null && !string.IsNullOrEmpty(device.name) && device.name.StartsWith("MobiView"))
            {
                devicesToRemove.Add(device);
            }
        }
        foreach (var device in devicesToRemove)
        {
            InputSystem.RemoveDevice(device);
        }

        _virtualTouch = InputSystem.AddDevice<Touchscreen>("MobiViewTouch");
        InputSystem.EnableDevice(_virtualTouch);

        _virtualAccel = InputSystem.AddDevice<Accelerometer>("MobiViewAccel");
        _virtualGyro = InputSystem.AddDevice<UnityEngine.InputSystem.Gyroscope>("MobiViewGyro");
        _virtualGravity = InputSystem.AddDevice<GravitySensor>("MobiViewGravity");
        _virtualAttitude = InputSystem.AddDevice<AttitudeSensor>("MobiViewAttitude");
        _virtualLinearAccel = InputSystem.AddDevice<LinearAccelerationSensor>("MobiViewLinearAccel");
    }

    public static void EnqueuePacket(byte[] data)
    {
        _queue.Enqueue(data);
    }

    public static void ProcessQueue()
    {
        while (_queue.TryDequeue(out var data))
        {
            if (data.Length < 4) continue;

            int count = data[1];
            ushort mask = BitConverter.ToUInt16(data, 2);
            int offset = 4;

            for (int i = 0; i < count; i++)
            {
                if (offset + 4 > data.Length) break;
                uint frameId = BitConverter.ToUInt32(data, offset);
                offset += 4;

                bool isNew = frameId > _lastFrameId;
                if (isNew) _lastFrameId = frameId;

                if ((mask & 1) != 0)
                {
                    if (offset >= data.Length) break;
                    byte touchCount = data[offset++];
                    for (int t = 0; t < touchCount; t++)
                    {
                        if (offset + 10 > data.Length) break;
                        if (isNew) ProcessTouch(data, offset);
                        offset += 10;
                    }
                }

                if ((mask & 2) != 0)
                {
                    if (offset + 12 > data.Length) break;
                    if (isNew)
                    {
                        var v = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), -BitConverter.ToSingle(data, offset + 8));
                        InputSystem.QueueDeltaStateEvent(_virtualAccel.acceleration, v);
                    }
                    offset += 12;
                }

                if ((mask & 4) != 0)
                {
                    if (offset + 12 > data.Length) break;
                    if (isNew)
                    {
                        var v = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), -BitConverter.ToSingle(data, offset + 8));
                        InputSystem.QueueDeltaStateEvent(_virtualGyro.angularVelocity, v);
                    }
                    offset += 12;
                }

                if ((mask & 8) != 0)
                {
                    if (offset + 12 > data.Length) break;
                    if (isNew)
                    {
                        var v = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), -BitConverter.ToSingle(data, offset + 8));
                        InputSystem.QueueDeltaStateEvent(_virtualGravity.gravity, v);
                    }
                    offset += 12;
                }

                if ((mask & 16) != 0)
                {
                    if (offset + 16 > data.Length) break;
                    if (isNew)
                    {
                        var q = new Quaternion(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), -BitConverter.ToSingle(data, offset + 8), -BitConverter.ToSingle(data, offset + 12));
                        InputSystem.QueueDeltaStateEvent(_virtualAttitude.attitude, q);
                    }
                    offset += 16;
                }

                if ((mask & 32) != 0)
                {
                    if (offset + 12 > data.Length) break;
                    if (isNew)
                    {
                        var v = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), -BitConverter.ToSingle(data, offset + 8));
                        InputSystem.QueueDeltaStateEvent(_virtualLinearAccel.acceleration, v);
                    }
                    offset += 12;
                }

                if ((mask & 64) != 0) offset += 12;
                if ((mask & 128) != 0) offset += 4;
                if ((mask & 256) != 0) offset += 4;
                if ((mask & 512) != 0) offset += 4;
                if ((mask & 1024) != 0) offset += 4;
                if ((mask & 2048) != 0) offset += 0; // TODO: Pen (Bit 11) - ustaw odpowiedni offset gdy zaimplementujesz w Swift
                if ((mask & 4096) != 0) offset += 0; // TODO: Gamepad (Bit 12) - ustaw odpowiedni offset gdy zaimplementujesz w Swift

                if ((mask & 8192) != 0) // Keyboard (Bit 13)
                {
                    if (offset >= data.Length) break;
                    byte keyCount = data[offset++];
                    offset += keyCount;
                }
            }
        }
    }

    private static void ProcessTouch(byte[] data, int offset)
    {
        if (_virtualTouch == null || !_virtualTouch.added)
            _virtualTouch = InputSystem.AddDevice<Touchscreen>("MobiViewTouch");

        int id = data[offset];
        int phaseByte = data[offset + 1];
        float x = BitConverter.ToSingle(data, offset + 2);
        float y = BitConverter.ToSingle(data, offset + 6);

        var phase = phaseByte switch
        {
            0 => UnityEngine.InputSystem.TouchPhase.Began,
            1 => UnityEngine.InputSystem.TouchPhase.Moved,
            2 => UnityEngine.InputSystem.TouchPhase.Ended,
            3 => UnityEngine.InputSystem.TouchPhase.Canceled,
            _ => UnityEngine.InputSystem.TouchPhase.None
        };
        if (phase == UnityEngine.InputSystem.TouchPhase.None) return;

        bool ending = phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    phase == UnityEngine.InputSystem.TouchPhase.Canceled;
        bool known = _activeTouches.Contains(id);

        if (ending)
        {
            if (!known) return;
            _activeTouches.Remove(id);
        }
        else if (!known)
        {
            phase = UnityEngine.InputSystem.TouchPhase.Began;
            _activeTouches.Add(id);
        }
        else if (phase == UnityEngine.InputSystem.TouchPhase.Began)
        {
            phase = UnityEngine.InputSystem.TouchPhase.Moved;
        }

        var state = new TouchState
        {
            touchId = id + 1,
            phase = phase,
            position = new Vector2(x * Screen.width, y * Screen.height),
            pressure = ending ? 0f : 1f
        };

        //debug
        DebugTouches[id] = new TouchDebugInfo
        {
            Position = state.position,
            Raw = phaseByte,
            Phase = phase,
            Time = UnityEngine.Time.unscaledTime
        };
        //debug

        InputSystem.QueueStateEvent(_virtualTouch, state);
    }
}


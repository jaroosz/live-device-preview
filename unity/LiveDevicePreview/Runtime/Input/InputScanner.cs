using UnityEngine;
using UnityEngine.InputSystem;

namespace LiveDevicePreview
{
    public static class InputScanner
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onDeviceChange += OnDeviceChange;
            UpdateMask();
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Removed:
                case InputDeviceChange.Enabled:
                case InputDeviceChange.Disabled:
                    UpdateMask();
                    break;
            }
        }

        public static void UpdateMask()
        {
            ushort mask = 0;

            foreach (var device in InputSystem.devices)
            {
                if (!device.enabled) continue;

                if (device is Touchscreen) mask |= 1 << 0;
                else if (device is Accelerometer) mask |= 1 << 1;
                else if (device is UnityEngine.InputSystem.Gyroscope) mask |= 1 << 2;
                else if (device is GravitySensor) mask |= 1 << 3;
                else if (device is AttitudeSensor) mask |= 1 << 4;
                else if (device is LinearAccelerationSensor) mask |= 1 << 5;
                else if (device is MagneticFieldSensor) mask |= 1 << 6;
                else if (device is LightSensor) mask |= 1 << 7;
                else if (device is ProximitySensor) mask |= 1 << 8;
                else if (device is PressureSensor) mask |= 1 << 9;
                else if (device is StepCounter) mask |= 1 << 10;
                else if (device is Pen) mask |= 1 << 11;
                else if (device is Gamepad) mask |= 1 << 12;
                else if (device is Keyboard) mask |= 1 << 13;
            }

            // Limit requested devices to what the connected client actually supports
            mask &= VideoStreamer.ClientSupportedSensors;

            VideoStreamer.RequiredInputMask = mask;
        }
    }
}


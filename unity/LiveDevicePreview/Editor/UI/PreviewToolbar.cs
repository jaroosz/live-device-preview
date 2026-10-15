using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;
#if UNITY_6000_7_OR_NEWER
using UnityEngine.UIElements;
#endif
using UnityEngine;

namespace LiveDevicePreview.Editor
{
    [InitializeOnLoad]
    internal static class PreviewToolbar
    {
        private const string ToolbarElementId = "LiveDevicePreview/PlayStream";
        private const string PlayMobileSessionKey = "LiveDevicePreview.PlayMobileSession";
        private const string VisibilityPreferenceKey = "LiveDevicePreview.Visible";
        private const string SelectedDeviceKey = "LiveDevicePreview.SelectedDevice";
        private const string OrientationKey = "LiveDevicePreview.Orientation";
        private const int OrientationPortrait = 0;
        private const int OrientationLandscape = 1;
        private const string QualityKey = "LiveDevicePreview.Quality";
        private const int QualityHigh = 100;
        private const string FpsKey = "LiveDevicePreview.Fps";
        private const int Fps30 = 30;

        private const string NormalIconName = "mobile-normal";
        private const string RedIconName = "mobile-red";
        private const string GreenIconName = "mobile-green";

#if UNITY_6000_7_OR_NEWER
        private const float DeviceButtonWidth = 150f;
#endif

        static PreviewToolbar()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Discovery.PhoneConnectionChanged += OnPhoneConnectionChanged;
            Discovery.SetVisible(EditorPrefs.GetBool(VisibilityPreferenceKey, false));
        }

        [MainToolbarElement(ToolbarElementId, defaultDockPosition = MainToolbarDockPosition.Middle)]
        private static IEnumerable<MainToolbarElement> CreatePlayStreamControls()
        {
            bool isPlaying = EditorApplication.isPlayingOrWillChangePlaymode;
            bool isStreamSession = SessionState.GetBool(PlayMobileSessionKey, false);
            string currentIconName = NormalIconName;

            if (isPlaying)
            {
                currentIconName = isStreamSession ? GreenIconName : RedIconName;
            }

            Texture2D icon = FindIcon(currentIconName);

            bool isVisible = EditorPrefs.GetBool(VisibilityPreferenceKey, false);
            string selectedDeviceId = EditorPrefs.GetString(SelectedDeviceKey, string.Empty);
            bool hasValidDevice = false;
            foreach (var phone in Discovery.ConnectedPhones)
            {
                if (phone.DeviceId == selectedDeviceId)
                {
                    hasValidDevice = true;
                    break;
                }
            }

            yield return new MainToolbarButton(
                new MainToolbarContent(icon, "Toggle Mobile Stream"),
                OnPlayMobileClicked)
            {
                enabled = isVisible && hasValidDevice
            };

#if UNITY_6000_7_OR_NEWER
            yield return new MainToolbarCustomElement(CreateDeviceDropdown);
#else
            yield return new MainToolbarDropdown(
                new MainToolbarContent(GetDeviceButtonText(), "Select target device"),
                ShowDeviceList);
#endif

            Texture2D gearIcon = EditorGUIUtility.IconContent("SettingsIcon").image as Texture2D;
            yield return new MainToolbarButton(
                new MainToolbarContent(gearIcon, "Stream settings"),
                ShowSettingsWindow);
        }

        private static string GetDeviceButtonText()
        {
            if (!EditorPrefs.GetBool(VisibilityPreferenceKey, false))
                return "Visibility OFF";

            if (Discovery.ConnectedPhones.Count == 0)
                return "Searching for device...";

            string selectedDeviceId = EditorPrefs.GetString(SelectedDeviceKey, string.Empty);
            foreach (var phone in Discovery.ConnectedPhones)
            {
                if (phone.DeviceId == selectedDeviceId)
                    return $"{phone.Model} ({phone.DeviceName})";
            }

            return "Select Device...";
        }

        private static void ShowDeviceList(Rect dropdownRect)
        {
            var menu = new GenericMenu();
            bool isVisible = EditorPrefs.GetBool(VisibilityPreferenceKey, false);

            if (!isVisible)
            {
                menu.AddDisabledItem(new GUIContent("Visibility is turned off."));
            }
            else if (Discovery.ConnectedPhones.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Visibility is turned on. Open the mobile app to connect."));
            }
            else
            {
                string selectedDeviceId = EditorPrefs.GetString(SelectedDeviceKey, string.Empty);
                foreach (var phone in Discovery.ConnectedPhones)
                {
                    bool isSelected = phone.DeviceId == selectedDeviceId;
                    if (SessionState.GetBool(PlayMobileSessionKey, false))
                    {
                        menu.AddDisabledItem(new GUIContent($"{phone.Model} ({phone.DeviceName})"), isSelected);
                    }
                    else
                    {
                        menu.AddItem(new GUIContent($"{phone.Model} ({phone.DeviceName})"), isSelected, () => SelectDevice(phone.DeviceId));
                    }
                }
            }
            menu.DropDown(dropdownRect);
        }

        private static void ShowSettingsWindow()
        {
            SettingsWindow.ShowWindow();
        }

        private static void SelectDevice(string deviceId)
        {
            EditorPrefs.SetString(SelectedDeviceKey, deviceId);
            RefreshToolbar();
        }

        private static void OnPlayMobileClicked()
        {
            if (!EditorPrefs.GetBool(VisibilityPreferenceKey, false))
                return;

            string selectedDeviceId = EditorPrefs.GetString(SelectedDeviceKey, string.Empty);
            bool hasValidDevice = false;
            foreach (var phone in Discovery.ConnectedPhones)
            {
                if (phone.DeviceId == selectedDeviceId)
                {
                    hasValidDevice = true;
                    break;
                }
            }

            if (!hasValidDevice)
                return;

            bool isPlaying = EditorApplication.isPlayingOrWillChangePlaymode;
            bool isStreamSession = SessionState.GetBool(PlayMobileSessionKey, false);
            if (!isPlaying)
            {
                SessionState.SetBool(PlayMobileSessionKey, true);
                EditorApplication.isPlaying = true;
            }
            else if (isStreamSession)
            {
                DisconnectStream();
                SessionState.SetBool(PlayMobileSessionKey, false);
            }
            else
            {
                PrepareForPlayMobile();
                SessionState.SetBool(PlayMobileSessionKey, true);
            }
            RefreshToolbar();
        }

        private static void PrepareForPlayMobile()
        {
            string selectedDeviceId = EditorPrefs.GetString(SelectedDeviceKey, string.Empty);
            PhoneInfo targetPhone = null;

            foreach (var phone in Discovery.ConnectedPhones)
            {
                if (phone.DeviceId == selectedDeviceId)
                {
                    targetPhone = phone;
                    break;
                }
            }

            if (targetPhone != null)
            {
                int orientation = EditorPrefs.GetInt(OrientationKey, OrientationPortrait);
                int minSize = Mathf.Min(targetPhone.Width, targetPhone.Height);
                int maxSize = Mathf.Max(targetPhone.Width, targetPhone.Height);

                int width = orientation == OrientationPortrait ? minSize : maxSize;
                int height = orientation == OrientationPortrait ? maxSize : minSize;

                int quality = EditorPrefs.GetInt(QualityKey, QualityHigh);
                int bitrate = quality switch
                {
                    25 => 1_000_000,   // Ultra Low: ~1 Mbps
                    50 => 4_000_000,   // Low: ~4 Mbps
                    75 => 8_000_000,   // Medium: ~8 Mbps
                    _ => 16_000_000    // High: ~16 Mbps
                };

                int targetFps = EditorPrefs.GetInt(FpsKey, Fps30);

                GameViewHelper.SetAspectRatio(width, height);
                FrameCapture.StartCapture(width, height, targetFps);
                VideoStreamer.ClientSupportedSensors = (ushort)targetPhone.SupportedSensors;
                InputScanner.UpdateMask();
                int udpPort = EditorPrefs.GetInt("LiveDevicePreview.UdpPort", 7778);
                VideoStreamer.Start(udpPort, bitrate);
            }
        }

        private static void DisconnectStream()
        {
            VideoStreamer.Stop();
            FrameCapture.StopCapture();
            GameViewHelper.RestoreResolution();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (SessionState.GetBool(PlayMobileSessionKey, false))
                    PrepareForPlayMobile();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (SessionState.GetBool(PlayMobileSessionKey, false))
                    DisconnectStream();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(PlayMobileSessionKey, false);
            }
            RefreshToolbar();
        }

        private static void OnPhoneConnectionChanged(PhoneInfo phone, bool connected)
        {
            RefreshToolbar();
        }

#if UNITY_6000_7_OR_NEWER
        private static VisualElement CreateDeviceDropdown()
        {
            EditorToolbarButton dropdown = null;
            dropdown = new EditorToolbarButton(GetDeviceButtonText(), () => ShowDeviceList(dropdown.worldBound));

            dropdown.style.width = DeviceButtonWidth;
            dropdown.style.minWidth = DeviceButtonWidth;
            dropdown.style.maxWidth = DeviceButtonWidth;
            dropdown.style.flexShrink = 0;
            dropdown.style.overflow = Overflow.Hidden;
            dropdown.style.whiteSpace = WhiteSpace.NoWrap;
            dropdown.style.unityTextAlign = TextAnchor.MiddleLeft;
            dropdown.style.justifyContent = Justify.FlexStart;
            dropdown.displayTooltipWhenElided = true;

            dropdown.schedule.Execute(() =>
            {
                string text = GetDeviceButtonText();
                if (dropdown.text != text)
                    dropdown.text = text;
            }).Every(250);

            return dropdown;
        }
#endif

        internal static void RefreshToolbar()
        {
            MainToolbar.Refresh(ToolbarElementId);
        }

        private static Texture2D FindIcon(string iconName)
        {
            string[] guids = AssetDatabase.FindAssets($"{iconName} t:Texture2D");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }
    }
}
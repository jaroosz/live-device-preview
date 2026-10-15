using UnityEditor;
using UnityEngine;

namespace LiveDevicePreview.Editor
{
    public class SettingsWindow : EditorWindow
    {
        private const string VisibilityPreferenceKey = "LiveDevicePreview.Visible";
        private const string OrientationKey = "LiveDevicePreview.Orientation";
        private const string QualityKey = "LiveDevicePreview.Quality";
        private const string FpsKey = "LiveDevicePreview.Fps";

        private const int OrientationPortrait = 0;
        private const int OrientationLandscape = 1;

        public static void ShowWindow()
        {
            var window = GetWindow<SettingsWindow>("Live Device Preview");
            window.minSize = new Vector2(300, 350);
            window.Show();
        }

        private void OnGUI()
        {
            bool isStreamSession = SessionState.GetBool("LiveDevicePreview.PlayMobileSession", false);

            EditorGUI.BeginDisabledGroup(isStreamSession);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);

            bool isVisible = EditorPrefs.GetBool(VisibilityPreferenceKey, false);

            EditorGUI.BeginDisabledGroup(isVisible);
            string currentHostName = EditorPrefs.GetString("LiveDevicePreview.HostName", string.Empty);
            string displayHostName = string.IsNullOrWhiteSpace(currentHostName) ? System.Environment.MachineName : currentHostName;
            string newHostName = EditorGUILayout.TextField("Editor Name", displayHostName);
            if (newHostName.Length > 32)
            {
                newHostName = newHostName.Substring(0, 32);
            }

            if (newHostName != displayHostName)
            {
                EditorPrefs.SetString("LiveDevicePreview.HostName", newHostName.Trim());
            }
            EditorGUI.EndDisabledGroup();

            bool newVisible = EditorGUILayout.Toggle("Editor visibility", isVisible);
            if (newVisible != isVisible)
            {
                EditorPrefs.SetBool(VisibilityPreferenceKey, newVisible);
                Discovery.SetVisible(newVisible);
            }

            bool showGizmos = EditorPrefs.GetBool("LiveDevicePreview.ShowDebugGizmos", true);
            bool newShowGizmos = EditorGUILayout.Toggle("Show Debug Gizmos", showGizmos);
            if (newShowGizmos != showGizmos)
                EditorPrefs.SetBool("LiveDevicePreview.ShowDebugGizmos", newShowGizmos);

            GUILayout.Space(15);
            EditorGUILayout.LabelField("Device Stream Profile", EditorStyles.boldLabel);

            int currentOrientation = EditorPrefs.GetInt(OrientationKey, OrientationPortrait);
            int newOrientation = EditorGUILayout.Popup("Orientation", currentOrientation, new[] { "Portrait", "Landscape" });
            if (newOrientation != currentOrientation)
                EditorPrefs.SetInt(OrientationKey, newOrientation);

            int currentQuality = EditorPrefs.GetInt(QualityKey, 100);
            int newQuality = EditorGUILayout.IntPopup("Stream Quality", currentQuality, new[] { "Ultra Low (1 Mbps)", "Low (4 Mbps)", "Medium (8 Mbps)", "High (16 Mbps)" }, new[] { 25, 50, 75, 100 });
            if (newQuality != currentQuality)
                EditorPrefs.SetInt(QualityKey, newQuality);

            int currentFps = EditorPrefs.GetInt(FpsKey, 30);
            int newFps = EditorGUILayout.IntPopup("Target Frame Rate", currentFps, new[] { "30 FPS", "60 FPS" }, new[] { 30, 60 });
            if (newFps != currentFps)
                EditorPrefs.SetInt(FpsKey, newFps);

            GUILayout.Space(15);
            EditorGUILayout.LabelField("Network & Advanced", EditorStyles.boldLabel);

            bool currentAutoPorts = EditorPrefs.GetBool("LiveDevicePreview.AutoPorts", true);
            bool newAutoPorts = EditorGUILayout.Toggle("Auto-manage Ports", currentAutoPorts);
            if (newAutoPorts != currentAutoPorts)
                EditorPrefs.SetBool("LiveDevicePreview.AutoPorts", newAutoPorts);

            EditorGUI.BeginDisabledGroup(newAutoPorts);
        
            int currentTcpPort = EditorPrefs.GetInt("LiveDevicePreview.TcpPort", 7777);
            int newTcpPort = EditorGUILayout.IntField("TCP Port (Control)", currentTcpPort);
            if (newTcpPort != currentTcpPort)
                EditorPrefs.SetInt("LiveDevicePreview.TcpPort", newTcpPort);

            int currentUdpPort = EditorPrefs.GetInt("LiveDevicePreview.UdpPort", 7778);
            int newUdpPort = EditorGUILayout.IntField("UDP Port (Stream)", currentUdpPort);
            if (newUdpPort != currentUdpPort)
                EditorPrefs.SetInt("LiveDevicePreview.UdpPort", newUdpPort);

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Hardware Codec", "H.265 (NVENC)");
            EditorGUI.EndDisabledGroup();
        
            EditorGUI.EndDisabledGroup();
        }
    }
}

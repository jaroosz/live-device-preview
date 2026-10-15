#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class MobiViewTouchOverlay : MonoBehaviour
{
    private const float MarkerSize = 40f;
    private const float EndedVisibleSeconds = 0.5f;

    private readonly List<int> _expired = new List<int>();
    private GUIStyle _labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        var go = new GameObject("MobiViewTouchOverlay");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<MobiViewTouchOverlay>();
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !MobiViewStreamer.IsRunning) return;
#if UNITY_EDITOR
        if (!UnityEditor.EditorPrefs.GetBool("MobiView.ShowDebugGizmos", true)) return;
#endif

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _labelStyle.normal.textColor = Color.white;
        }

        var now = Time.unscaledTime;
        var receivedActive = 0;
        _expired.Clear();

        foreach (var pair in MobiViewInputReceiver.DebugTouches)
        {
            var info = pair.Value;
            var ended = info.Phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                        info.Phase == UnityEngine.InputSystem.TouchPhase.Canceled;

            if (ended && now - info.Time > EndedVisibleSeconds)
            {
                _expired.Add(pair.Key);
                continue;
            }

            if (!ended) receivedActive++;

            var center = new Vector2(info.Position.x, Screen.height - info.Position.y);
            GUI.color = ended ? new Color(1f, 0f, 0f, 0.6f) : new Color(0f, 1f, 0f, 0.6f);
            GUI.DrawTexture(
                new Rect(center.x - MarkerSize / 2f, center.y - MarkerSize / 2f, MarkerSize, MarkerSize),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(center.x + MarkerSize / 2f + 4f, center.y - 12f, 260f, 24f),
                $"id {pair.Key}  raw {info.Raw}  {info.Phase}",
                _labelStyle);
        }

        foreach (var id in _expired)
            MobiViewInputReceiver.DebugTouches.Remove(id);

        var inProgress = 0;
        var touchscreen = MobiViewInputReceiver.VirtualTouch;
        if (touchscreen != null && touchscreen.added)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (touch.isInProgress) inProgress++;
            }
        }

        GUI.Label(
            new Rect(10f, 100f, 500f, 24f),
            $"received active: {receivedActive}   touchscreen in progress: {inProgress}",
            _labelStyle);
    }
}
#endif
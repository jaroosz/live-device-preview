using System;
using System.Collections;
using UnityEngine;

public sealed class MobiViewCapture : MonoBehaviour
{
    public static event Action<IntPtr, int, int> OnFrameCaptured;

    /// <summary>Stream frame rate requested by the user (used by the encoder's rate control).</summary>
    public static int TargetFps { get; private set; } = 30;

    private static MobiViewCapture _instance;
    private RenderTexture _renderTexture;
    private float _captureInterval;

    public static void StartCapture(int width, int height, int fps)
    {
        if (_instance != null) return;

        var go = new GameObject("MobiViewCapture");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);

        TargetFps = Mathf.Max(1, fps);

        _instance = go.AddComponent<MobiViewCapture>();
        _instance._captureInterval = 1f / fps;

        // In Linear color space the backbuffer is sRGB. Capturing into a non-sRGB (Linear) RT makes
        // the GPU decode sRGB->linear on read and store the *linear* values, which the encoder/phone
        // then interpret as sRGB -> image looks darker with more contrast. An sRGB RT re-encodes on
        // write, so the bytes match what the Game View shows. CopyResource into the B8G8R8A8_UNORM
        // NVENC texture is a raw copy within the same typeless group, so sRGB bytes are preserved.
        // In Gamma color space the backbuffer is not sRGB, so a plain UNORM RT is correct there.
        var format = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB
            : UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_UNorm;
        _instance._renderTexture = new RenderTexture(width, height, format, UnityEngine.Experimental.Rendering.GraphicsFormat.None);
        _instance._renderTexture.filterMode = FilterMode.Point;
        _instance._renderTexture.Create();
        
        _instance.StartCoroutine(_instance.CaptureRoutine());
    }

    public static void StopCapture()
    {
        if (_instance != null)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    private void Update()
    {
        MobiViewInputReceiver.ProcessQueue();
    }

    private IEnumerator CaptureRoutine()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;

            if (_renderTexture != null && OnFrameCaptured != null)
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_renderTexture);
                OnFrameCaptured.Invoke(_renderTexture.GetNativeTexturePtr(), _renderTexture.width, _renderTexture.height);
            }

            if (_captureInterval > 0)
            {
                yield return new WaitForSecondsRealtime(_captureInterval);
            }
        }
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }
}

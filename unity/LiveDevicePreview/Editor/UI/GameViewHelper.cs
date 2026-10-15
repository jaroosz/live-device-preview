using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LiveDevicePreview.Editor
{
    internal static class GameViewHelper
    {
        private static int _previousSizeIndex = -1;
        private const string SizeLabel = "Live Device Preview";

        public static void SetAspectRatio(int width, int height)
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var gameViewSizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");

            if (gameViewType == null || gameViewSizesType == null) return;
            var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
            if (gameView == null) return;

            var scriptableSingletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
            var sizesInstance = scriptableSingletonType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
            var currentGroup = gameViewSizesType.GetProperty("currentGroup", BindingFlags.Instance | BindingFlags.Public).GetValue(sizesInstance, null);
            var groupType = currentGroup.GetType();

            var displayTexts = (string[])groupType.GetMethod("GetDisplayTexts").Invoke(currentGroup, null);
            int targetIndex = -1;

            for (int i = 0; i < displayTexts.Length; i++)
            {
                if (displayTexts[i].StartsWith(SizeLabel))
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex != -1)
            {
                // Edit existing
                var sizeObj = groupType.GetMethod("GetGameViewSize").Invoke(currentGroup, new object[] { targetIndex });
                sizeObj.GetType().GetProperty("width").SetValue(sizeObj, width, null);
                sizeObj.GetType().GetProperty("height").SetValue(sizeObj, height, null);
            }
            else
            {
                // Create new
                var sizeTypeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType");
                var constructor = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize").GetConstructor(new[] { sizeTypeType, typeof(int), typeof(int), typeof(string) });
                var newSize = constructor.Invoke(new[] { Enum.Parse(sizeTypeType, "FixedResolution"), width, height, SizeLabel });
                groupType.GetMethod("AddCustomSize").Invoke(currentGroup, new[] { newSize });
                targetIndex = displayTexts.Length;
            }

            var selectedSizeIndexProp = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_previousSizeIndex == -1)
                _previousSizeIndex = (int)selectedSizeIndexProp.GetValue(gameView, null);

            var sizeSelectionCallback = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (sizeSelectionCallback != null)
                sizeSelectionCallback.Invoke(gameView, new object[] { targetIndex, null });
            else
                selectedSizeIndexProp.SetValue(gameView, targetIndex, null);

            gameView.Repaint();
            gameView.Focus();
        }

        public static void RestoreResolution()
        {
            if (_previousSizeIndex != -1)
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView != null)
                {
                    var sizesInstance = typeof(ScriptableSingleton<>).MakeGenericType(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes")).GetProperty("instance").GetValue(null, null);
                    var currentGroup = sizesInstance.GetType().GetProperty("currentGroup").GetValue(sizesInstance, null);
                    int totalCount = (int)currentGroup.GetType().GetMethod("GetTotalCount").Invoke(currentGroup, null);

                    if (_previousSizeIndex < totalCount)
                    {
                        var sizeSelectionCallback = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (sizeSelectionCallback != null)
                            sizeSelectionCallback.Invoke(gameView, new object[] { _previousSizeIndex, null });
                        else
                            gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(gameView, _previousSizeIndex, null);
                    }

                    gameView.Repaint();
                }
                _previousSizeIndex = -1;
            }
        }
    }
}


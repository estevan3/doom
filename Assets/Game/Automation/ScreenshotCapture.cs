using System.IO;
using UnityEngine;

namespace DoomClone.Automation
{
    /// <summary>Captures runtime screenshots from the game camera into PNG files on disk.</summary>
    public static class ScreenshotCapture
    {
        public static Camera GetGameCamera()
        {
            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null && player.playerCamera != null) return player.playerCamera;

            Camera main = Camera.main;
            if (main != null) return main;

            return UnityEngine.Object.FindAnyObjectByType<Camera>();
        }

        public static string CaptureMain(string absolutePath, int width = 1280, int height = 720)
        {
            return CaptureToFile(absolutePath, GetGameCamera(), width, height);
        }

        public static string CaptureToFile(string absolutePath, Camera camera, int width = 1280, int height = 720)
        {
            if (camera == null || string.IsNullOrEmpty(absolutePath)) return null;

            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;

                Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();

                byte[] png = texture.EncodeToPNG();
                Object.Destroy(texture);

                File.WriteAllBytes(absolutePath, png);
                return absolutePath;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.Destroy(renderTexture);
            }
        }
    }
}
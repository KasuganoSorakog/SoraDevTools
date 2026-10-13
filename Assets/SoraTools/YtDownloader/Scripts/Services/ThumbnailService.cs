using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace YtDownloader
{
    /// <summary>
    /// Downloads a video thumbnail texture using UnityWebRequestTexture.
    /// Must be driven by a MonoBehaviour coroutine (main thread).
    /// Compatible with Unity 2019+.
    /// </summary>
    public class ThumbnailService
    {
        private MonoBehaviour _host;

        public ThumbnailService(MonoBehaviour host)
        {
            _host = host;
        }

        public void DownloadThumbnail(string url, Action<Texture2D> onDone)
        {
            if (string.IsNullOrEmpty(url)) { onDone?.Invoke(null); return; }
            _host.StartCoroutine(DownloadRoutine(url, onDone));
        }

        private IEnumerator DownloadRoutine(string url, Action<Texture2D> onDone)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool success = req.result == UnityWebRequest.Result.Success;
#else
                bool success = !req.isNetworkError && !req.isHttpError;
#endif
                if (success)
                    onDone?.Invoke(DownloadHandlerTexture.GetContent(req));
                else
                {
                    Debug.LogWarning("[ThumbnailService] Failed: " + req.error);
                    onDone?.Invoke(null);
                }
            }
        }
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace YoutubeTools
{
    /// <summary>
    /// Downloads a video thumbnail texture using UnityWebRequestTexture.
    /// Must be driven by a MonoBehaviour coroutine (main thread).
    /// </summary>
    public class ThumbnailService
    {
        private MonoBehaviour _host;

        public ThumbnailService(MonoBehaviour host)
        {
            _host = host;
        }

        /// <summary>
        /// Begin downloading the thumbnail. onDone is called on the main thread
        /// with the resulting texture (or null on failure).
        /// </summary>
        public void DownloadThumbnail(string url, Action<Texture2D> onDone)
        {
            if (string.IsNullOrEmpty(url))
            {
                onDone?.Invoke(null);
                return;
            }

            _host.StartCoroutine(DownloadRoutine(url, onDone));
        }

        private IEnumerator DownloadRoutine(string url, Action<Texture2D> onDone)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(req);
                    onDone?.Invoke(tex);
                }
                else
                {
                    Debug.LogWarning($"[ThumbnailService] Failed to download thumbnail: {req.error}");
                    onDone?.Invoke(null);
                }
            }
        }
    }
}

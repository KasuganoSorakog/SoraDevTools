using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SoraTools.HeadsetWifi
{
    internal sealed class HeadsetWifiMainThread : MonoBehaviour
    {
        private static readonly object SyncRoot = new object();
        private static readonly Queue<Action> PendingActions = new Queue<Action>();
        private static HeadsetWifiMainThread instance;
        private static int mainThreadId;

        public static void Initialize()
        {
            if (instance != null)
                return;

            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var host = new GameObject("[SoraTools.HeadsetWifiDispatcher]");
            instance = host.AddComponent<HeadsetWifiMainThread>();
            DontDestroyOnLoad(host);
        }

        public static Task<T> RunAsync<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception exception)
                {
                    var failed = new TaskCompletionSource<T>();
                    failed.SetException(exception);
                    return failed.Task;
                }
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        public static void Enqueue(Action action)
        {
            if (action == null)
                return;

            lock (SyncRoot)
            {
                PendingActions.Enqueue(action);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void Update()
        {
            while (true)
            {
                Action action;
                lock (SyncRoot)
                {
                    if (PendingActions.Count == 0)
                        return;
                    action = PendingActions.Dequeue();
                }

                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}

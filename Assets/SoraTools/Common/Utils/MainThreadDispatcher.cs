using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoraTools.Utils
{
    /// <summary>
    /// Thread-safe dispatcher for executing actions on Unity's main thread.
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static readonly object _lock = new object();
        private readonly List<Action> _executing = new List<Action>();

        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[SoraTools.MainThreadDispatcher]");
                    _instance = go.AddComponent<MainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_lock)
            {
                _queue.Enqueue(action);
            }
        }

        void Update()
        {
            lock (_lock)
            {
                if (_queue.Count == 0) return;
                while (_queue.Count > 0)
                    _executing.Add(_queue.Dequeue());
            }

            foreach (var action in _executing)
            {
                try { action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _executing.Clear();
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}

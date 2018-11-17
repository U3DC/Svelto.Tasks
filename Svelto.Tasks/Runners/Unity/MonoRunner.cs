#if UNITY_5 || UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using Svelto.DataStructures;
using Svelto.Tasks.Unity.Internal;
using UnityEngine;

namespace Svelto.Tasks.Unity
{
    
    /// Remember, unless you are using the StandardSchedulers, nothing hold your runners. Be careful that if you
    /// don't hold a reference, they will be garbage collected even if tasks are still running
    /// </summary>
    
    public abstract class MonoRunner<T> : IRunner<T> where T:IEnumerator
    {
        public bool paused { set; get; }
        public bool isStopping { get { return _flushingOperation.stopped; } }
        public bool isKilled { get {return _go == null;} }
        public int  numberOfRunningTasks { get { return _coroutines.Count; } }
        
        public GameObject _go;

        private MonoRunner()
        {}

        protected MonoRunner(string name)
        {
            _name = name;
        }

        ~MonoRunner()
        {
            Svelto.Utilities.Console.LogWarning("MonoRunner has been garbage collected, this could have serious" +
                                                "consequences, are you sure you want this? ".FastConcat(_name));
            
            StopAllCoroutines();
        }
        
        /// <summary>
        /// TaskRunner doesn't stop executing tasks between scenes
        /// it's the final user responsibility to stop the tasks if needed
        /// </summary>
        public virtual void StopAllCoroutines()
        {
            paused = false;

            UnityCoroutineRunner<T>.StopRoutines(_flushingOperation);

            _newTaskRoutines.Clear();
        }

        public virtual void StartCoroutine(SveltoTask<T> task)
        {
            paused = false;

            _newTaskRoutines.Enqueue(task); //careful this could run on another thread!
        }

        public virtual void Dispose()
        {
            StopAllCoroutines();
            
            GameObject.DestroyImmediate(_go);
            _go = null;
            GC.SuppressFinalize(this);
        }
        
        protected readonly ThreadSafeQueue<SveltoTask<T>> _newTaskRoutines = new ThreadSafeQueue<SveltoTask<T>>();
        protected readonly FasterList<SveltoTask<T>> _coroutines =
            new FasterList<SveltoTask<T>>(NUMBER_OF_INITIAL_COROUTINE);
        
        protected UnityCoroutineRunner<T>.FlushingOperation _flushingOperation =
            new UnityCoroutineRunner<T>.FlushingOperation();

        string _name;

        const int NUMBER_OF_INITIAL_COROUTINE = 3;
    }
}
#endif

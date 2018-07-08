#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.DataStructures;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    /// <summary>
    /// while you can istantiate a MonoRunner, you should use the standard one
    /// whenever possible. Istantiating multiple runners will defeat the
    /// initial purpose to get away from the Unity monobehaviours
    /// internal updates. MonoRunners are disposable though, so at
    /// least be sure to dispose of them once done
    /// </summary>

    public class LateMonoRunner : LateMonoRunner<IEnumerator>, IRunner
    {
        public LateMonoRunner(string name) : base(name)
        {
        }
    }
    
    public class LateMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public LateMonoRunner(string name)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go);

            var coroutines = new FasterList<PausableTask<T>>(NUMBER_OF_INITIAL_COROUTINE);
            var runnerBehaviour = _go.AddComponent<RunnerBehaviourLate>();
            var runnerBehaviourForUnityCoroutine = _go.AddComponent<RunnerBehaviour>();

            _info = new UnityCoroutineRunner<T>.RunningTasksInfo() { runnerName = name };

            runnerBehaviour.StartLateCoroutine(UnityCoroutineRunner<T>.Process
                (_newTaskRoutines, coroutines, _flushingOperation, _info,
                 UnityCoroutineRunner<T>.StandardTasksFlushing,
                 runnerBehaviourForUnityCoroutine, StartCoroutine));
        }

        protected override UnityCoroutineRunner<T>.RunningTasksInfo info
        { get { return _info; } }

        protected override ThreadSafeQueue<PausableTask<T>> newTaskRoutines
        { get { return _newTaskRoutines; } }

        protected override UnityCoroutineRunner<T>.FlushingOperation flushingOperation
        { get { return _flushingOperation; } }

        readonly ThreadSafeQueue<PausableTask<T>>         _newTaskRoutines   = new ThreadSafeQueue<PausableTask<T>>();
        readonly UnityCoroutineRunner<T>.FlushingOperation _flushingOperation = new UnityCoroutineRunner<T>.FlushingOperation();
        readonly UnityCoroutineRunner<T>.RunningTasksInfo  _info;
      
        const int NUMBER_OF_INITIAL_COROUTINE = 3;
    }
}
#endif
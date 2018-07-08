#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.DataStructures;
using Svelto.Tasks.Internal;

//SequentialMonoRunner doesn't execute the next
//coroutine in the queue until the previous one is completed

namespace Svelto.Tasks
{
    /// <summary>
    /// while you can istantiate a MonoRunner, you should use the standard one
    /// whenever possible. Istantiating multiple runners will defeat the
    /// initial purpose to get away from the Unity monobehaviours
    /// internal updates. MonoRunners are disposable though, so at
    /// least be sure to dispose of them once done
    /// </summary>
    public class SequentialMonoRunner: SequentialMonoRunner<IEnumerator>, IRunner
    {
        public SequentialMonoRunner(string name) : base(name)
        {}
    }
    
    public class SequentialMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public SequentialMonoRunner(string name)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go);

            var coroutines = new FasterList<PausableTask<T>>(NUMBER_OF_INITIAL_COROUTINE);
            var runnerBehaviour = _go.AddComponent<RunnerBehaviourUpdate>();
            var runnerBehaviourForUnityCoroutine = _go.AddComponent<RunnerBehaviour>();

            _info = new UnityCoroutineRunner<T>.RunningTasksInfo { runnerName = name };

            runnerBehaviour.StartUpdateCoroutine(UnityCoroutineRunner<T>.Process
            (_newTaskRoutines, coroutines, _flushingOperation, _info,
                SequentialTasksFlushing,
                runnerBehaviourForUnityCoroutine, StartCoroutine));
        }

        protected override UnityCoroutineRunner<T>.RunningTasksInfo info
        { get { return _info; } }

        protected override ThreadSafeQueue<PausableTask<T>> newTaskRoutines
        { get { return _newTaskRoutines; } }

        protected override UnityCoroutineRunner<T>.FlushingOperation flushingOperation
        { get { return _flushingOperation; } }
        
        static void SequentialTasksFlushing(
            ThreadSafeQueue<PausableTask<T>> newTaskRoutines, 
            FasterList<PausableTask<T>> coroutines, 
            UnityCoroutineRunner<T>.FlushingOperation flushingOperation)
        {
            if (newTaskRoutines.Count > 0 && coroutines.Count == 0)
                newTaskRoutines.DequeueInto(coroutines, 1);
        }

        readonly ThreadSafeQueue<PausableTask<T>>         _newTaskRoutines   = new ThreadSafeQueue<PausableTask<T>>();
        readonly UnityCoroutineRunner<T>.FlushingOperation _flushingOperation = new UnityCoroutineRunner<T>.FlushingOperation();
        readonly UnityCoroutineRunner<T>.RunningTasksInfo  _info;
      
        const int NUMBER_OF_INITIAL_COROUTINE = 3;
    }
}
#endif
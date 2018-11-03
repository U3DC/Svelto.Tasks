#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.DataStructures;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{
    /// <summary>
    //SequentialMonoRunner doesn't execute the next
    //coroutine in the queue until the previous one is completed
    /// </summary>
    public class SequentialMonoRunner: SequentialMonoRunner<IEnumerator>, IRunner
    {
        public SequentialMonoRunner(string name) : base(name)
        {}
    }
    
    public class SequentialMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public SequentialMonoRunner(string name, bool mustSurvive = false)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourUpdate>();
            var info = new UnityCoroutineRunner.StandardRunningTaskInfo { runnerName = name };

            runnerBehaviour.StartUpdateCoroutine(new UnityCoroutineRunner.Process
            (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }

        static void SequentialTasksFlushing(
            ThreadSafeQueue<PausableTask<T>> newTaskRoutines, 
            FasterList<PausableTask<T>> coroutines, 
            UnityCoroutineRunner<T>.FlushingOperation flushingOperation)
        {
            if (newTaskRoutines.Count > 0 && coroutines.Count == 0)
                newTaskRoutines.DequeueInto(coroutines, 1);
        }
    }
}
#endif
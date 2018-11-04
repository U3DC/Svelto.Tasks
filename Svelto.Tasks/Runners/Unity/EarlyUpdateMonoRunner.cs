using System.Collections;
using Svelto.Tasks.Unity.Internal;

#if UNITY_5 || UNITY_5_3_OR_NEWER

namespace Svelto.Tasks.Unity
{
    public class EarlyUpdateMonoRunner : EarlyUpdateMonoRunner<IEnumerator>
    {
        public EarlyUpdateMonoRunner(UpdateMonoRunner updateRunner, string name) : base(updateRunner, name)
        {
        }
    }
    
    public class EarlyUpdateMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public EarlyUpdateMonoRunner(UpdateMonoRunner<T> updateRunner, string name)
        {
            _go = updateRunner._go;

            var runnerBehaviour = _go.GetComponent<RunnerBehaviourUpdate>();

            var info = new UnityCoroutineRunner<T>.StandardRunningTaskInfo { runnerName = name };

            runnerBehaviour.StartEarlyUpdateCoroutine(new UnityCoroutineRunner<T>.Process
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }
    }
}
#endif
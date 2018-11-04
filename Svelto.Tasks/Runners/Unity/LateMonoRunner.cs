#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{

    public class LateMonoRunner : LateMonoRunner<IEnumerator>
    {
        public LateMonoRunner(string name, bool mustSurvive = false) : base(name, mustSurvive)
        {
        }
    }
    
    public class LateMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public LateMonoRunner(string name, bool mustSurvive = false)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourLate>();
            var info = new UnityCoroutineRunner<T>.StandardRunningTaskInfo() { runnerName = name };

            runnerBehaviour.StartLateCoroutine(new UnityCoroutineRunner<T>.Process
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }
    }
}
#endif
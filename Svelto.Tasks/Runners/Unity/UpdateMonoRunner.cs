using Svelto.Tasks.Unity.Internal;

#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;

namespace Svelto.Tasks.Unity
{
    ///
    public class UpdateMonoRunner : UpdateMonoRunner<IEnumerator>
    {
        public UpdateMonoRunner(string name, bool mustSurvive = false) : base(name, mustSurvive)
        {
        }
    }
    public class UpdateMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public UpdateMonoRunner(string name, bool mustSurvive = false)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourUpdate>();
            
            var info = new UnityCoroutineRunner<T>.StandardRunningTaskInfo { runnerName = name };

            runnerBehaviour.StartUpdateCoroutine(new UnityCoroutineRunner<T>.Process
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }
    }
}
#endif
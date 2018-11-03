using Svelto.Tasks.Unity.Internal;

#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;

namespace Svelto.Tasks.Unity
{
    ///
    public class UpdateMonoRunner : UpdateMonoRunner<IEnumerator>, IRunner
    {
        public UpdateMonoRunner(string name) : base(name)
        {
        }
    }
    public class UpdateMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public UpdateMonoRunner(string name, bool mustSurvive = false)
        {
            UnityCoroutineRunner.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourUpdate>();
            
            var info = new UnityCoroutineRunner.StandardRunningTaskInfo { runnerName = name };

            runnerBehaviour.StartUpdateCoroutine(new UnityCoroutineRunner.Process
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }
    }
}
#endif
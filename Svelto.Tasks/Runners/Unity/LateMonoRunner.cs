#if UNITY_5 || UNITY_5_3_OR_NEWER
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{

    public class LateMonoRunner : LateMonoRunner<IEnumerator>, IRunner
    {
        public LateMonoRunner(string name) : base(name)
        {
        }
    }
    
    public class LateMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public LateMonoRunner(string name, bool mustSurvive = false)
        {
            UnityCoroutineRunner.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourLate>();
            var info = new UnityCoroutineRunner.StandardRunningTaskInfo() { runnerName = name };

            runnerBehaviour.StartLateCoroutine(new UnityCoroutineRunner.Process
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }
    }
}
#endif
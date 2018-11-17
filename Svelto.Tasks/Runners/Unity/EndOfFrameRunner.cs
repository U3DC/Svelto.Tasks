#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{
    public class EndOfFrameRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public EndOfFrameRunner(string name, bool mustSurvive = false):base(name)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourEndOfFrame>();
            var info = new UnityCoroutineRunner<T>.RunningTasksInfo() { runnerName = name };

            runnerBehaviour.StartEndOfFrameCoroutine(new UnityCoroutineRunner<T>.Process<UnityCoroutineRunner<T>.RunningTasksInfo>
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }

    }
}
#endif
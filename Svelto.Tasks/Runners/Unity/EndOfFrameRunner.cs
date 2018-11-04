#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{
    public class EndOfFrameRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public EndOfFrameRunner(string name, bool mustSurvive = false)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourEndOfFrame>();
            var info = new UnityCoroutineRunner<T>.StandardRunningTaskInfo() { runnerName = name };

            runnerBehaviour.StartEndOfFrameCoroutine(new UnityCoroutineRunner<T>.Process
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }

    }
}
#endif
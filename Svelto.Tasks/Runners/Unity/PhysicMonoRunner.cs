#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{
    /// <summary>
    /// while you can istantiate a MonoRunner, you should use the standard one whenever possible. Istantiating multiple
    /// runners will defeat the initial purpose to get away from the Unity monobehaviours internal updates.
    /// MonoRunners are disposable though, so at least be sure to dispose of them once done
    /// </summary>
    ///
    public class PhysicMonoRunner:PhysicMonoRunner<IEnumerator> 
    {
        public PhysicMonoRunner(string name, bool mustSurvive = false) : base(name, mustSurvive)
        {
        }
    }
    public class PhysicMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public PhysicMonoRunner(string name, bool mustSurvive = false):base(name)
        {
            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourPhysic>();
            var info = new UnityCoroutineRunner<T>.RunningTasksInfo() { runnerName = name };

            runnerBehaviour.StartPhysicCoroutine(new UnityCoroutineRunner<T>.Process<UnityCoroutineRunner<T>.RunningTasksInfo>
                (_newTaskRoutines, _coroutines, _flushingOperation, info));
        }
    }
}
#endif
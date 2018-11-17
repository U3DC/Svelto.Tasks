#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using System.Diagnostics;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{
    /// <summary>
    ///TimeBoundMonoRunner ensures that the tasks running won't take more than maxMilliseconds per iteration
    ///Several tasks must run on this runner to make sense. TaskCollections are considered
    ///single tasks, so they don't count (may change in future)
    /// </summary>
    public class TimeBoundMonoRunner : TimeBoundMonoRunner<IEnumerator>
    {
        public TimeBoundMonoRunner(string name, float maxMilliseconds, bool mustSurvive = false) : base(name, maxMilliseconds, mustSurvive)
        {}
    }
    
    public class TimeBoundMonoRunner<T> : MonoRunner<T> where T:IEnumerator
    {
        public float maxMilliseconds
        {
            set
            {
                _info.maxMilliseconds = (long) (value * 10000);
            }
        }

        // Greedy means that the runner will try to occupy the whole maxMilliseconds interval, by looping among all tasks until all are completed or maxMilliseconds passed
        public TimeBoundMonoRunner(string name, float maxMilliseconds, bool mustSurvive = false):base(name)
        {
            _flushingOperation = new UnityCoroutineRunner<T>.FlushingOperation();

            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourUpdate>();

            _info = new TimeBoundRunningInfo(maxMilliseconds)
            {
                runnerName = name
            };

            runnerBehaviour.StartUpdateCoroutine(new UnityCoroutineRunner<T>.Process<TimeBoundRunningInfo>
                (_newTaskRoutines, _coroutines, _flushingOperation, _info));
        }

        class TimeBoundRunningInfo : IRunningTasksInfo
        {
            public long maxMilliseconds;

            public TimeBoundRunningInfo(float maxMilliseconds)
            {
                this.maxMilliseconds = (long) (maxMilliseconds * 10000);
            }
            
            public bool CanMoveNext(ref int nextIndex, object currentResult)
            {
                if (_stopWatch.ElapsedTicks > maxMilliseconds)
                    return false;
                 
                return true;
            }

            public bool CanProcessThis(ref int index)
            {
                return true;
            }

            public void Reset()
            {
                _stopWatch.Reset();
                _stopWatch.Start();
            }

            public string runnerName { get; set; }

            readonly Stopwatch _stopWatch = new Stopwatch();

        }

        readonly TimeBoundRunningInfo _info;
    }
}
#endif
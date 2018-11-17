#if UNITY_5 || UNITY_5_3_OR_NEWER
using System.Collections;
using System.Diagnostics;
using Svelto.DataStructures;
using Svelto.Tasks.Unity.Internal;

namespace Svelto.Tasks.Unity
{
    /// <summary>
    //TimeSlicedMonoRunner ensures that the tasks running run up to the maxMilliseconds time.
    //If a task takes less than it, the next one will be executed and so on until maxMilliseconds is reached.
    //TimeSlicedMonoRunner can work with one task only too, this means that it would force the task to run up
    //to maxMilliseconds per frame, unless this returns Break.AndResumeIteration.
    /// </summary>
    
    public class TimeSlicedMonoRunner : TimeSlicedMonoRunner<IEnumerator>
    {
        public TimeSlicedMonoRunner(string name, float maxMilliseconds, bool mustSurvive = false) : base(name, maxMilliseconds, mustSurvive)
        {}
    }
    
    public class TimeSlicedMonoRunner<T>: MonoRunner<T> where T:IEnumerator
    {
        public float maxMilliseconds
        {
            set
            {
                _info.maxTicks = (long) (value * 10000);
            }
        }

        public TimeSlicedMonoRunner(string name, float maxMilliseconds, bool mustSurvive = false):base(name)
        {
            _flushingOperation = new UnityCoroutineRunner<T>.FlushingOperation();

            UnityCoroutineRunner<T>.InitializeGameObject(name, ref _go, mustSurvive);

            var runnerBehaviour = _go.AddComponent<RunnerBehaviourUpdate>();
            
            _info = new GreedyTimeBoundRunningInfo<T>(maxMilliseconds, _coroutines) { runnerName = name };
            
            runnerBehaviour.StartUpdateCoroutine(new UnityCoroutineRunner<T>.Process<GreedyTimeBoundRunningInfo<T>>
                (_newTaskRoutines, _coroutines, _flushingOperation, _info));
        }

        class GreedyTimeBoundRunningInfo<T> : IRunningTasksInfo where T:IEnumerator
        {
            public long maxTicks;

            public GreedyTimeBoundRunningInfo(float maxMilliseconds, FasterList<SveltoTask<T>> coroutines)
            {
                this.maxTicks = (long) (maxMilliseconds * 10000);
                _coroutines = coroutines;
            }

            public bool CanMoveNext(ref int nextIndex, object currentResult)
            {
                //never stops until maxMilliseconds is elapsed or Break.AndResumeNextIteration is returned
                if (_stopWatch.ElapsedTicks > maxTicks)
                {
                    _stopWatch.Reset();
                    _stopWatch.Start();
                    
                    return false;
                }
                
                if (currentResult == Break.RunnerExecutionAndResumeNextIteration)
                    mustInterruptAtTheEndOfTheIterations = true;
                
                if (nextIndex >= _coroutines.Count)
                {
                    if (mustInterruptAtTheEndOfTheIterations)
                    {
                        mustInterruptAtTheEndOfTheIterations = false;
                        return false;
                    }

                    nextIndex = 0; //restart iteration and continue
                }
                
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
            readonly FasterList<SveltoTask<T>> _coroutines;
            bool mustInterruptAtTheEndOfTheIterations;
        }

        readonly GreedyTimeBoundRunningInfo<T> _info;
    }
}
#endif
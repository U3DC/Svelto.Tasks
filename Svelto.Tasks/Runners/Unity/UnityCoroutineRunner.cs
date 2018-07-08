#if UNITY_5 || UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using Svelto.DataStructures;
using UnityEngine;
using Object = UnityEngine.Object;
#if TASKS_PROFILER_ENABLED && UNITY_EDITOR
using Svelto.Tasks.Profiler;
#endif


namespace Svelto.Tasks.Internal
{
    public static class UnityCoroutineRunner<T> where T:IEnumerator
    {
        public static void StandardTasksFlushing(ThreadSafeQueue<PausableTask<T>> newTaskRoutines, 
            FasterList<PausableTask<T>> coroutines, FlushingOperation flushingOperation)
        {
            if (newTaskRoutines.Count > 0)
                newTaskRoutines.DequeueAllInto(coroutines);
        }

        public static void StopRoutines(FlushingOperation
            flushingOperation)
        {
            //note: _coroutines will be cleaned by the single tasks stopping silently.
            //in this way they will be put back to the pool.
            //let's be sure that the runner had the time to stop and recycle the previous tasks
            flushingOperation.stopped = true;
        }

        internal static void InitializeGameObject(string name, ref GameObject go)
        {
            var taskRunnerName = "TaskRunner.".FastConcat(name);

            DBC.Tasks.Check.Require(GameObject.Find(taskRunnerName) == null, GAMEOBJECT_ALREADY_EXISTING_ERROR);

            go = new GameObject(taskRunnerName);
#if UNITY_EDITOR
            if (Application.isPlaying)
#endif
                Object.DontDestroyOnLoad(go);
        }

        internal static IEnumerator Process(ThreadSafeQueue<PausableTask<T>> newTaskRoutines,
                                            FasterList<PausableTask<T>> coroutines, 
                                            FlushingOperation flushingOperation,
                                            RunningTasksInfo info, 
                                            FlushTasksDel flushTasks)
        {
            return Process(newTaskRoutines, coroutines, 
                flushingOperation, info, flushTasks, null, null);
        }

        internal static IEnumerator Process(
            ThreadSafeQueue<PausableTask<T>> newTaskRoutines,
            FasterList<PausableTask<T>> coroutines, 
            FlushingOperation flushingOperation,
            RunningTasksInfo info,
            FlushTasksDel flushTasks,
            RunnerBehaviour runnerBehaviourForUnityCoroutine,
            Action<PausableTask<T>> 
            resumeOperation)
        {
            while (true)
            {
                if (false == flushingOperation.stopped) //don't start anything while flushing
                    flushTasks(newTaskRoutines, coroutines, flushingOperation);
                else
                if (runnerBehaviourForUnityCoroutine != null)
                    runnerBehaviourForUnityCoroutine.StopAllCoroutines();

                info.count = coroutines.Count;

                for (var i = 0; i < info.count; i++)
                {
                    var TaskRoutine = coroutines[i];

                    //let's spend few words on this. 
                    //yielded YieldInstruction and AsyncOperation can 
                    //only be processed internally by Unity. 
                    //The simplest way to handle them is to hand them to Unity itself.
                    //However while the Unity routine is processed, the rest of the 
                    //coroutine is waiting for it. This would defeat the purpose 
                    //of the parallel procedures. For this reason, a Parallel
                    //task will mark the enumerator returned as ParallelYield which 
                    //will change the way the routine is processed.
                    //in this case the MonoRunner won't wait for the Unity routine 
                    //to continue processing the next tasks.
                    //Note that it is much better to return wrap AsyncOperation around
                    //custom IEnumerator classes then returning them directly as
                    //most of the time they don't need to be handled by Unity as
                    //YieldInstructions do
                    
                    ///
                    /// Handle special Unity instructions
                    /// you should avoid them or wrap them
                    /// around custom IEnumerator to avoid
                    /// the cost of two allocations per instruction
                    /// 

                    if (runnerBehaviourForUnityCoroutine != null && 
                        flushingOperation.stopped == false)
                    {
                        var current = (TaskRoutine as TaskCollection<T>.CollectionTask);

                        var YieldReturn = (current).current;

                        if (YieldReturn is YieldInstruction)
                        {
                            var handItToUnity = new HandItToUnity(YieldReturn);

                            //questo deve cambiare
                            current.Add((T) handItToUnity.WaitUntilIsDone());

                            var coroutine = runnerBehaviourForUnityCoroutine.StartCoroutine
                                (handItToUnity.GetEnumerator());
                            
                            TaskRoutine.onExplicitlyStopped = () =>
                            {
                                runnerBehaviourForUnityCoroutine.StopCoroutine(coroutine);
                                handItToUnity.ForceStop();
                            };
                        }
                    }                        

                    
                    //move next coroutine step
                    bool mustContinue;
#if TASKS_PROFILER_ENABLED && UNITY_EDITOR
                    result = TASK_PROFILER.MonitorUpdateDuration(TaskRoutine, info.runnerName);
#else
                    mustContinue = TaskRoutine.MoveNext();
#endif
                    if (mustContinue == false)
                    {
                        var disposable = TaskRoutine as IDisposable;
                        if (disposable != null)
                            disposable.Dispose();

                        coroutines.UnorderedRemoveAt(i--);
                    }

                    info.count = coroutines.Count;
                }

                if (flushingOperation.stopped == true && coroutines.Count == 0)
                {   //once all the coroutines are flushed
                    //the loop can return accepting new tasks
                    flushingOperation.stopped = false;
                }

                yield return null;
            }
        }

        public class RunningTasksInfo
        {
            public int count;
            public string runnerName;
        }

        internal delegate void FlushTasksDel(ThreadSafeQueue<PausableTask<T>> 
            newTaskRoutines, FasterList<PausableTask<T>> coroutines, 
            FlushingOperation flushingOperation);

        public class FlushingOperation
        {
            public bool stopped;
        }

        struct HandItToUnity
        {
            public HandItToUnity(object current,
                TaskRoutine<T> task,
                Action<TaskRoutine<T>> resumeOperation,
                FlushingOperation flush)
            {
                _current = current;
                _task = task;
                _resumeOperation = resumeOperation;
                _isDone = false;
                _flushingOperation = flush;
            }

            public HandItToUnity(object current)
            {
                _current = current;
                _resumeOperation = null;
                _task = default(TaskRoutine<T>);
                _isDone = false;
                _flushingOperation = null;
            }

            public IEnumerator GetEnumerator()
            {
                yield return _current;

                ForceStop();
            }
            
            public void ForceStop()
            {
                _isDone = true;
                
                if (_flushingOperation != null &&
                    _flushingOperation.stopped == false &&
                    _resumeOperation != null)
                    _resumeOperation(_task);
            }

            public IEnumerator WaitUntilIsDone()
            {
                while (_isDone == false)
                    yield return null;
            }

            readonly object                _current;
            readonly TaskRoutine<T>         _task;
            readonly Action<TaskRoutine<T>> _resumeOperation;

            bool              _isDone;
            FlushingOperation _flushingOperation;
        }
        
#if TASKS_PROFILER_ENABLED && UNITY_EDITOR
        public static readonly TaskProfiler TASK_PROFILER = new TaskProfiler();
#endif        
        const string GAMEOBJECT_ALREADY_EXISTING_ERROR = "A MonoRunner GameObject with the same name was already been used, did you forget to dispose the old one?";
    }
}
#endif
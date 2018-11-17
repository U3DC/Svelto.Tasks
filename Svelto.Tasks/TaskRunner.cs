using System.Collections;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    public class TaskRunner
    {
        static TaskRunner _instance;

        public static TaskRunner Instance
        {
            get
            {
                if (_instance == null)
                    InitInstance();

                return _instance;
            }
        }
        
        public IRunner<IEnumerator> standardRunner { get; private set; }

        /// <summary>
        /// Use this function only to preallocate TaskRoutine that can be reused. this minimize run-time allocations
        /// </summary>
        /// <returns>
        /// New reusable TaskRoutine
        /// </returns>
        public TaskRoutine<T> AllocateNewTaskRoutine<T>(IRunner<T> runner) where T:IEnumerator
        {
            return new TaskRoutine<T>(new SveltoTask<T>(runner));
        }
        
        public TaskRoutine<IEnumerator> AllocateNewTaskRoutine(IRunner<IEnumerator> runner)
        {
            return new TaskRoutine<IEnumerator>(new SveltoTask<IEnumerator>(runner));
        }
        
        public TaskRoutine<IEnumerator> AllocateNewTaskRoutine()
        {
            return new TaskRoutine<IEnumerator>(new SveltoTask<IEnumerator>(standardRunner));
        }
        
        public void PauseAllTasks()
        {
            standardRunner.paused = true;
        }

        public void ResumeAllTasks()
        {
            standardRunner.paused = false;
        }

        public void Run(IEnumerator task)
        {
            RunOnScheduler(standardRunner, task);
        }

        public void RunOnScheduler(IRunner<IEnumerator> runner, IEnumerator task)
        {
            _taskPool.RetrieveTaskFromPool().SetEnumerator(task).SetScheduler(runner).Start();
        }

        public static void StopAndCleanupAllDefaultSchedulers()
        {
            StandardSchedulers.KillSchedulers();

            if (_instance != null)
            {
                _instance._taskPool = null;
                _instance.standardRunner   = null;
                _instance = null;
            }
        }

         static void InitInstance()
         {
            _instance = new TaskRunner();
#if UNITY_5_3_OR_NEWER || UNITY_5
            _instance.standardRunner = StandardSchedulers.coroutineScheduler;
#else
            _instance._runner = new MultiThreadRunner("TaskThread");
#endif
            _instance._taskPool = new PausableTaskPool();

//must still find the right place for this
#if TASKS_PROFILER_ENABLED && UNITY_EDITOR
            var debugTasksObject = UnityEngine.GameObject.Find("Svelto.Tasks.Profiler");
            if (debugTasksObject == null)
            {
                debugTasksObject = new UnityEngine.GameObject("Svelto.Tasks.Profiler");
                debugTasksObject.gameObject.AddComponent<Svelto.Tasks.Profiler.TasksProfilerBehaviour>();
                UnityEngine.Object.DontDestroyOnLoad(debugTasksObject);
            }
#endif
        }

        PausableTaskPool             _taskPool;
    }
}

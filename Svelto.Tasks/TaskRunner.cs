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

        /// <summary>
        /// Use this function only to preallocate TaskRoutine that can be reused. this minimize run-time allocations
        /// </summary>
        /// <returns>
        /// New reusable TaskRoutine
        /// </returns>
        public TaskRoutine<T> AllocateNewTaskRoutine<T>(IRunner<T> runner) where T:IEnumerator
        {
            return new TaskRoutine<T>(new PausableTask<T>(runner));
        }
        
        public TaskRoutine<IEnumerator> AllocateNewTaskRoutine() 
        {
            return new TaskRoutine<IEnumerator>(new PausableTask<IEnumerator>(_runner));
        }
        
        public void PauseAllTasks()
        {
            _runner.paused = true;
        }

        public void ResumeAllTasks()
        {
            _runner.paused = false;
        }

        public void Run(IEnumerator task)
        {
            RunOnSchedule(_runner, task);
        }

        public void RunOnSchedule(IRunner runner, IEnumerator task)
        {
            var pausableTask = _taskPool.RetrieveTaskFromPool();
            pausableTask.SetRunner(runner);
            pausableTask.SetEnumerator(task);
            pausableTask.Start();
        }

        public static void StopAndCleanupAllDefaultSchedulers()
        {
            StandardSchedulers.KillSchedulers();

            if (_instance != null)
            {
                _instance._taskPool = null;
                _instance._runner   = null;
                _instance = null;
            }
        }

         static void InitInstance()
         {
            _instance = new TaskRunner();
#if UNITY_5_3_OR_NEWER || UNITY_5
            _instance._runner = StandardSchedulers.coroutineScheduler;
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

        IRunner          _runner;
        PausableTaskPool _taskPool;
    }
}

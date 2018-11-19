using System.Collections;

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

        public SveltoTask<T> AllocateNewTaskRoutine<T>(IRunner<T> runner) where T:IEnumerator
        {
            var sveltoTask = new SveltoTask<T>(runner);
            return sveltoTask;
        }
        
        public SveltoTask<IEnumerator> AllocateNewTaskRoutine(IRunner<IEnumerator> runner)
        {
            var sveltoTask = new SveltoTask<IEnumerator>(runner);
            return sveltoTask;
        }
        
        public SveltoTask<IEnumerator> AllocateNewTaskRoutine()
        {
            var sveltoTask = new SveltoTask<IEnumerator>(standardRunner);
            return sveltoTask;
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

        public void RunOnScheduler(IRunner<IEnumerator> runner, IEnumerator task) //todo may return continuationwrapper
        {
            var allocateNewTaskRoutine = AllocateNewTaskRoutine(runner);
            allocateNewTaskRoutine.SetEnumerator(task);
            allocateNewTaskRoutine.Start();
        }

        public static void StopAndCleanupAllDefaultSchedulers()
        {
            StandardSchedulers.KillSchedulers();

            if (_instance != null)
            {
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
    }
}

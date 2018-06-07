#if UNITY_5 || UNITY_5_3_OR_NEWER
using Svelto.DataStructures;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    /// <summary>
    /// while you can istantiate a MonoRunner, you should use the standard one
    /// whenever possible. Istantiating multiple runners will defeat the
    /// initial purpose to get away from the Unity monobehaviours
    /// internal updates. MonoRunners are disposable though, so at
    /// least be sure to dispose of them once done
    /// </summary>
    public class CoroutineMonoRunner : MonoRunner
    {
        public CoroutineMonoRunner(string name)
        {
            UnityCoroutineRunner.InitializeGameObject(name, ref _go);
            var coroutines = new FasterList<IPausableTask>(NUMBER_OF_INITIAL_COROUTINE);

            RunnerBehaviour runnerBehaviour = _go.AddComponent<RunnerBehaviour>();
            var runnerBehaviourForUnityCoroutine = _go.AddComponent<RunnerBehaviour>();

            _info = new UnityCoroutineRunner.RunningTasksInfo() { runnerName = name };

            runnerBehaviour.StartCoroutine(UnityCoroutineRunner.Process
                (_newTaskRoutines, coroutines, _flushingOperation, _info,
                 UnityCoroutineRunner.StandardTasksFlushing,
                 runnerBehaviourForUnityCoroutine, StartCoroutine));
        }
        
        public override void StartCoroutine(IPausableTask task)
        {
            paused = false;

            if (ExecuteFirstTaskStep(task) == true)
                newTaskRoutines.Enqueue(task); //careful this could run on another thread!
        }
        
        bool ExecuteFirstTaskStep(IPausableTask task)
        {
            if (task == null)
                return false;

            //if the runner is not ready to run new tasks, it
            //cannot run immediatly but it must be saved
            //in the newTaskRoutines to be executed once possible
            if (isStopping == true)
                return true;
            
#if TASKS_PROFILER_ENABLED && UNITY_EDITOR
            return UnityCoroutineRunner.TASK_PROFILER.MonitorUpdateDuration(task, info.runnerName);
#else
            return task.MoveNext();
#endif
        }

        protected override UnityCoroutineRunner.RunningTasksInfo info
        { get { return _info; } }

        protected override ThreadSafeQueue<IPausableTask> newTaskRoutines
        { get { return _newTaskRoutines; } }

        protected override UnityCoroutineRunner.FlushingOperation flushingOperation
        { get { return _flushingOperation; } }

        readonly ThreadSafeQueue<IPausableTask>         _newTaskRoutines = new ThreadSafeQueue<IPausableTask>();
        readonly UnityCoroutineRunner.FlushingOperation _flushingOperation = new UnityCoroutineRunner.FlushingOperation();
        readonly UnityCoroutineRunner.RunningTasksInfo  _info;
      
        const int NUMBER_OF_INITIAL_COROUTINE = 3;
    }
}
#endif
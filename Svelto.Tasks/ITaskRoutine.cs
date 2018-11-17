using System;
using System.Collections;

//ITaskRoutine allocated explcitly have several features not 
//available on task started implicitly with the extension
//methods.

//TaskRoutine are promises compliant. However the use of Start 
//and Stop can generate different behaviours

//Start();
//Stop();
//Start();

//The new Start will not run immediatly, but will let the Task to stop first
//and trigger the callback;

//Start();
//Start();

//allows to start the task immediatly but the OnStop callback won't be triggered

namespace Svelto.Tasks
{
    public struct TaskRoutine<T> where T:IEnumerator
    {
        internal TaskRoutine(SveltoTask<T> pausableTask)
        {
            _pausableTask = pausableTask;
        }
        
        public TaskRoutine<T> SetEnumeratorProvider(Func<T> taskGenerator)
        {
            _pausableTask.SetEnumeratorProvider(taskGenerator);

            return this;
        }

        public TaskRoutine<T> SetEnumerator(T taskGenerator)
        {
            _pausableTask.SetEnumerator(taskGenerator);
            
            return this;
        }
        
        public TaskRoutine<T> SetScheduler(IRunner<T> runner)
        {
            _pausableTask.SetScheduler(runner);
            
            return this;
        }

        public ContinuationWrapper<T> Start(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            return _pausableTask.StartRoutine(onFail, onStop);
        }
        
        public void Pause()
        {
            _pausableTask.Pause();
        }

        public void Resume()
        {
            _pausableTask.Resume();
        }

        public void Stop()
        {
            _pausableTask.Stop();
        }

        public bool isRunning
        {
            get { return _pausableTask.isRunning; }
        }
        
        readonly SveltoTask<T> _pausableTask;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;

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
    public interface ITaskRoutine<T> where T:IEnumerator
    {
        void SetEnumeratorProvider(Func<T> taskGenerator);
        void SetEnumerator(T taskGenerator);
        void SetEnumeratorRef(ref T taskGenerator);

        ContinuationWrapper<T> Start(Action<PausableTaskException> onFail = null, Action onStop = null);
     
        void Pause();
        void Resume();
        void Stop();
        
        bool isRunning { get; }
    }
}

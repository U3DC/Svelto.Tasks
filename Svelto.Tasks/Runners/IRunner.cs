using System;
using System.Collections;

namespace Svelto.Tasks
{
    public interface IRunnerInterface : IDisposable
    {
        bool paused     { get; set; }
        bool isStopping { get; }

        void StopAllCoroutines();

        int numberOfRunningTasks { get; }
    }
    
    public interface IRunner<T>: IRunnerInterface where T:IEnumerator
    {
        void StartCoroutine(SveltoTask<T> task);
    }
}

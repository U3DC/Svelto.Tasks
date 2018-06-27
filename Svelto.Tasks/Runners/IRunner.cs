using System;
using System.Collections;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    public interface IRunner : IDisposable
    {
        bool paused     { get; set; }
        bool isStopping { get; }

        void StopAllCoroutines();

        int numberOfRunningTasks { get; }
    }
    
    public interface IRunner<T>: IRunner where T:IEnumerator
    {
        void	StartCoroutine(IPausableTask<T> task);
    }
}

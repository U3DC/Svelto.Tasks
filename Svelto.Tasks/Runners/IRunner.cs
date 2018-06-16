using System;
using System.Collections;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    public interface IRunner : IRunner<IEnumerator>
    {}
    
    public interface IRunner<T>: IDisposable where T:IEnumerator
    {
        bool    paused { get; set; }
        bool    isStopping { get; }

        void	StartCoroutine(IPausableTask<T> task);
        void 	StopAllCoroutines();

        int numberOfRunningTasks { get; }
    }
}

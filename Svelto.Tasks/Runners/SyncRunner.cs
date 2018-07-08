
using System.Collections;
using Svelto.Tasks.Internal;
using Svelto.Utilities;

namespace Svelto.Tasks
{
    //Be sure you know what you are doing when you are using
    //the Sync runner, it will stall the current thread!
    //Depending by the case, it may be better to
    //use the ManualResetEventEx synchronization instead.

    public class SyncRunner : SyncRunner<IEnumerator>, IRunner
    {}

    public class SyncRunner<T> : IRunner<T> where T:IEnumerator
    {
        public void StartCoroutine(PausableTask<T> task)
        {
            while (task.MoveNext() == true) ThreadUtility.Yield();
        }
        
        public bool paused     { get; set; }
        public bool isStopping { get; }
        
        public void StopAllCoroutines()
        {
            throw new System.NotImplementedException();
        }

        public int numberOfRunningTasks { get; }

        public void Dispose()
        {}
    }
}

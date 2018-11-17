using System.Collections;
namespace Svelto.Tasks
{

    /// <summary>
    /// Be sure you know what you are doing when you are using the Sync runner, it will stall the current thread!
    /// Depending by the case, it may be better to use the ManualResetEventEx synchronization instead. 
    /// </summary>

    public class SyncRunner : SyncRunner<IEnumerator>
    {
        public bool isKilled { get { return false; } }
    }

    public class SyncRunner<T> : IRunner<T> where T:IEnumerator
    {
        public void StartCoroutine(SveltoTask<T> task)
        {
            task.Complete();
        }
        
        public bool paused     { get; set; }
        public bool isStopping { get; }
        public bool isKilled { get; }

        /// TaskRunner doesn't stop executing tasks between scenes it's the final user responsibility to stop the
        /// tasks if needed
        public void StopAllCoroutines() { throw new System.NotImplementedException(); }

        public int numberOfRunningTasks { get; }

        public void Dispose()
        {}
    }
}

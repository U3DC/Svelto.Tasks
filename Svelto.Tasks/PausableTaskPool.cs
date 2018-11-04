using System.Collections;
using Svelto.DataStructures;

namespace Svelto.Tasks.Internal
{
    public sealed class PausableTaskPool
    {
        public SveltoTask<> RetrieveTaskFromPool()
        {
            SveltoTask<> task;

            if (_pool.Dequeue(out task))
                return task;

            return CreateEmptyTask();
        }

        public void PushTaskBack(SveltoTask<> task)
        {
            _pool.Enqueue(task);
        }

        SveltoTask<> CreateEmptyTask()
        {
            return new SveltoTask<>(this);
        }

        readonly LockFreeQueue<SveltoTask<>> _pool = new LockFreeQueue<SveltoTask<>>();
    }
}

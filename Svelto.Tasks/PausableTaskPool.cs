using Svelto.DataStructures;

namespace Svelto.Tasks.Internal
{
    sealed class PausableTaskPool
    {
        public PausableTask RetrieveTaskFromPool()
        {
            PausableTask task;

            if (_pool.Dequeue(out task))
                return task;

            return CreateEmptyTask();
        }

        public void PushTaskBack(PausableTask task)
        {
            task.Reset();

            _pool.Enqueue(task);
        }

        PausableTask CreateEmptyTask()
        {
            return new PausableTask(this);
        }

        readonly LockFreeQueue<PausableTask> _pool = new LockFreeQueue<PausableTask>();
    }
}

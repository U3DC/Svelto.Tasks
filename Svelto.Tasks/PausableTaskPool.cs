using System.Collections;
using Svelto.DataStructures;

namespace Svelto.Tasks.Internal
{
    public sealed class PausableTaskPool
    {
        public TaskRoutine<IEnumerator> RetrieveTaskFromPool()
        {
            TaskRoutine<IEnumerator> task;

            if (_pool.Dequeue(out task))
                return task;

            return CreateEmptyTask();
        }

        public void PushTaskBack(SveltoTask task)
        {
            _pool.Enqueue(new TaskRoutine<IEnumerator>(task));
        }

        TaskRoutine<IEnumerator> CreateEmptyTask()
        {
            return new TaskRoutine<IEnumerator> (new SveltoTask(this));
        }

        readonly LockFreeQueue<TaskRoutine<IEnumerator> > _pool = new LockFreeQueue<TaskRoutine<IEnumerator> >();
    }
}

using System.Collections;
using Svelto.DataStructures;

namespace Svelto.Tasks.Internal
{
    public sealed class TaskStackPool<T> where T:IEnumerator
    {
        public SerialTaskCollection<T> RetrieveTaskFromPool()
        {
            SerialTaskCollection<T> task;

            if (_pool.Dequeue(out task))
                return task;

            return CreateEmptyTask();
        }

        public void PushTaskBack(SerialTaskCollection<T> task)
        {
            task.Clear();
            
            _pool.Enqueue(task);
        }

        SerialTaskCollection<T> CreateEmptyTask()
        {
            return new SerialTaskCollection<T>();
        }

        readonly LockFreeQueue<SerialTaskCollection<T>> _pool = new LockFreeQueue<SerialTaskCollection<T>>();
    }
}

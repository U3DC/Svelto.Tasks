using System;
using System.Collections;
using Svelto.DataStructures;

namespace Svelto.Tasks.Unity.Internal
{
    public static class StandardCoroutineProcess
    {
        public static void StandardCoroutineIteration<T>(ref int i, FasterList<PausableTask<T>> coroutines) where T:IEnumerator
        {
            var pausableTask = coroutines[i];

            bool result;
#if TASKS_PROFILER_ENABLED
            result = Svelto.Tasks.Profiler.TaskProfiler.MonitorUpdateDuration(pausableTask, _info.runnerName);
#else
            result = pausableTask.MoveNext();
#endif
            if (result == false)
            {
                var disposable = pausableTask as IDisposable;
                if (disposable != null)
                    disposable.Dispose();

                coroutines.UnorderedRemoveAt(i--);
            }
        }
    }
}
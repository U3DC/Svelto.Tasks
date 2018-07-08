using Svelto.Tasks;
using System.Collections;
using Svelto.Utilities;

public static class TaskRunnerExtensions
{
    public static void RunOnSchedule(this IEnumerator enumerator, IRunner runner)
    {
        TaskRunner.Instance.RunOnSchedule(runner, enumerator);
    }
    
    public static void Run(this IEnumerator enumerator)
    {
        TaskRunner.Instance.Run(enumerator);
    }
    
    public static void Complete<T>(this T enumerator) where T:IEnumerator
    {
        while (enumerator.MoveNext()) ThreadUtility.Yield();
    }
    
    public static TaskRoutine<T> AllocateNewTaskRoutine<T>(this T enumerator, IRunner<T> runner) where T:IEnumerator
    {
        return TaskRunner.Instance.AllocateNewTaskRoutine<T>(runner).SetEnumeratorRef(ref enumerator);
    }
}

using Svelto.Tasks;
using System.Collections;
using Svelto.Utilities;

public static class TaskRunnerExtensions
{
    public static void RunOnScheduler(this IEnumerator enumerator, IRunner<IEnumerator> runner)
    {
        TaskRunner.Instance.RunOnScheduler(runner, enumerator);
    }

    public static void Run(this IEnumerator enumerator)
    {
        TaskRunner.Instance.Run(enumerator);
    }
    
    public static void Complete<T>(this T enumerator) where T:IEnumerator
    {
        var quickIterations = 0;
        
        while (enumerator.MoveNext())
            ThreadUtility.Wait(ref quickIterations);
    }
    
    public static TaskRoutine<T> ToTaskRoutine<T>(this T enumerator, IRunner<T> runner) where T:IEnumerator
    {
        return TaskRunner.Instance.AllocateNewTaskRoutine(runner).SetEnumerator(enumerator);
    }
    
    public static TaskRoutine<IEnumerator> ToTaskRoutine(this IEnumerator enumerator)
    {
        return TaskRunner.Instance.AllocateNewTaskRoutine(TaskRunner.Instance.standardRunner).SetEnumerator(enumerator);
    }
}

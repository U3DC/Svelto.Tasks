using Svelto.Tasks;
using System.Collections;
using System.Collections.Generic;
using Svelto.Utilities;

public static class TaskRunnerExtensions
{
    public static void RunOnSchedule<T>(this T enumerator, IRunner<T> runner) where T:class, IEnumerator<object>
    {
        TaskRunner.Instance.RunOnSchedule(runner, enumerator);
    }
    
    public static void Run<T>(this T enumerator) where T:class, IEnumerator<object>
    {
        TaskRunner.Instance.Run(enumerator);
    }
    
    public static void Complete<T>(this T enumerator) where T:IEnumerator
    {
        while (enumerator.MoveNext()) ThreadUtility.Yield();
    }
    
    public static ITaskRoutine<T> AllocateNewRoutine<T>(this T enumerator) where T:IEnumerator
    {
        return TaskRunner.Instance.AllocateNewTaskRoutine<T>().SetEnumeratorRef(ref enumerator);
    }
}


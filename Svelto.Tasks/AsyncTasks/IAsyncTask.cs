using System;

namespace Svelto.Tasks
{
    /*
     * ITask is the interface of a generic Task
     * a Task is not meant to be enumerated, it
     * must be executed only once!
     * A task can (and should) wrap an 
     * asynchronous call though, so it can
     * last over the time.
     * once the task is completed, the isDone
     * value must be set to true.
     **/
    
    public interface IAbstractAsyncTask
    {
        bool isDone { get; }
    }
    
    public interface IAsyncTask:IAbstractAsyncTask
    {
        void Execute();
    }
    
    public interface IAsyncTask<Token>:IAbstractAsyncTask
    {
        void Execute(ref Token token);
    }
#if TO_IMPLEMENT_PROPERLY
    public interface ITaskProgress
    {
        float		progress { get; }
    }
#endif
    public interface IAsyncTaskExceptionHandler
    {
        Exception   throwException { get; }
    }
}


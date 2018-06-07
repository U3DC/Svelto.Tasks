namespace Svelto.Tasks
{
    //an enumerator returning Break.It breaks only the running task collection enumeration but allows the parent task
    //to continue
    //returning yield break would instead stops only the current enumerator
    //returning BreakAndStop bubble until it gets to the starting ITaskRoutine
    //which is stopped and triggers the OnStop callback
    public class Break
    {
        public static readonly Break It = new Break();
        public static readonly Break AndStop = new Break();
    }
}

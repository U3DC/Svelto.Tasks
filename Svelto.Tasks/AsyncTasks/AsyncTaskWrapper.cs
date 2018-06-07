using System;
using System.Collections;
using Utility;

namespace Svelto.Tasks.Internal
{
    sealed class AsyncTaskWrapper<Token>: IEnumerator
    {
        public object Current { get { return null; } }

        public AsyncTaskWrapper(IAbstractAsyncTask asyncTask, Action<ActionRef<Token, IAbstractAsyncTask>, IAbstractAsyncTask> token)
        {
            DBC.Tasks.Check.Require((asyncTask is IEnumerable == false) && (asyncTask is IEnumerator == false), "Tasks and IEnumerators are mutually exclusive");

            this._asyncTask = asyncTask;
            _tokenAction = token;
            
            DBC.Tasks.Check.Ensure(asyncTask != null, "a valid task must be assigned");
        }
        
        public AsyncTaskWrapper(IAbstractAsyncTask asyncTask)
        {
            DBC.Tasks.Check.Require((asyncTask is IEnumerable == false) && (asyncTask is IEnumerator == false), "Tasks and IEnumerators are mutually exclusive");

            this._asyncTask = asyncTask;
            
            DBC.Tasks.Check.Ensure(asyncTask != null, "a valid task must be assigned");
        }

        public bool MoveNext()
        {
            if (_started == false)
            {
                _tokenAction(_executeTask, _asyncTask);

                _started = true;
            }
            
            if (_asyncTask.isDone == false)
            {
                var taskException = _asyncTask as IAsyncTaskExceptionHandler;

                if ((taskException != null) && (taskException.throwException != null))
                    throw taskException.throwException;

                return true;
            }

            _started = false;

            return false;
        }

        public void Reset()
        {
            throw new NotImplementedException("Async Tasks cannot be reset");
        }

        public override string ToString()
        {
            return _asyncTask.ToString();
        }

        static void ExecuteTask(ref Token token, ref IAbstractAsyncTask asyncTask)
        {
            if (asyncTask is IAsyncTask<Token>)
                (asyncTask as IAsyncTask<Token>).Execute(ref token);    
            else
            if (asyncTask is IAsyncTask)
                (asyncTask as IAsyncTask).Execute();
            else
                throw new Exception("not supported task " + asyncTask.GetType());
        }

        readonly IAbstractAsyncTask _asyncTask;

        bool _started;

        readonly Action<ActionRef<Token, IAbstractAsyncTask>, IAbstractAsyncTask> _tokenAction;
        readonly ActionRef<Token, IAbstractAsyncTask> _executeTask = ExecuteTask;
    }
}


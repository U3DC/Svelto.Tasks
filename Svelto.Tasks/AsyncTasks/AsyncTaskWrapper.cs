using System;
using System.Collections;

namespace Svelto.Tasks.Internal
{
    sealed class AsyncTaskWrapper: IEnumerator
    {
        public object Current { get { return null; } }

        public AsyncTaskWrapper(IAsyncTask asyncTask)
        {
            DBC.Tasks.Check.Require((asyncTask is IEnumerable == false) && (asyncTask is IEnumerator == false), "Tasks and IEnumerators are mutually exclusive");

            _asyncTask = asyncTask;
            
            DBC.Tasks.Check.Ensure(asyncTask != null, "a valid task must be assigned");
        }
        

        public bool MoveNext()
        {
            if (_started == false)
            {
                _asyncTask.Execute();

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

        readonly IAsyncTask _asyncTask;
        bool _started;
    }
}


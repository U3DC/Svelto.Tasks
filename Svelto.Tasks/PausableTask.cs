///
/// Unit tests to write:
/// Restart a task with compiled generated IEnumerator
/// Restart a task with IEnumerator class
/// Restart a task after SetEnumerator has been called (this must be still coded, as it must reset some values)
/// Restart a task just restarted (pendingRestart == true)
/// Start a taskroutine twice with different compiler generated enumerators and variants
/// 
///
/// 
using Svelto.Utilities;
using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace Svelto.Tasks
{
    public class PausableTaskException : Exception
    {
        public PausableTaskException(Exception e)
            : base(e.ToString(), e)
        { }
    }

    public interface IPausableTask:IEnumerator
    {
        Action onExplicitlyStopped { set; }
    }
    
    public class ContinuationWrapper:IEnumerator
    {
        public bool MoveNext()
        {
            ThreadUtility.MemoryBarrier();
            var result = completed == true;
            if (_condition != null)
                result |= _condition();
            
            if (result == true)
            {
                _condition = null;
                _completed = false;
                ThreadUtility.MemoryBarrier();
                return false;
            }
            
            return true;
        }
        
        public void BreakOnCondition(Func<bool> func)
        {
            _condition = func;
        }

        internal void Completed()
        {
            _completed = true;
            ThreadUtility.MemoryBarrier();
        }
        
        public bool completed
        {
            get { return _completed; }
        }        

        public void Reset()
        {
            _completed = false;
            ThreadUtility.MemoryBarrier();
        }

        public object Current { get { return null; } }

        volatile bool _completed;
        Func<bool> _condition;
    }

}

namespace Svelto.Tasks.Internal
{
    class PausableTask : PausableTask<IEnumerator>, ITaskRoutine
    {
        public new ITaskRoutine SetScheduler(IRunner runner)
        {
            base.SetScheduler(runner);

            return this;
        }

        public new  ITaskRoutine SetEnumeratorProvider(Func<IEnumerator> taskGenerator)
        {
            base.SetEnumeratorProvider(taskGenerator);

            return this;
        }
        
        public new  ITaskRoutine SetEnumerator(IEnumerator taskEnumerator)
        {
            base.SetEnumerator(taskEnumerator);

            return this;
        }
    }
    
    class PausableTask<T> : IPausableTask, ITaskRoutine<T> where T:IEnumerator
    {    
        const string CALL_START_FIRST_ERROR = "Enumerating PausableTask without starting it, please call Start() first";

        public Action onExplicitlyStopped { private get; set; }

        /// <summary>
        /// Calling SetScheduler, SetEnumeratorProvider, SetEnumerator
        /// on a running task won't stop the task until either 
        /// Stop() or Start() is called.
        /// </summary>
        /// <param name="runner"></param>
        /// <returns></returns>
        public ITaskRoutine<T> SetScheduler(IRunner runner)
        {
            _runner = runner;

            return this;
        }

        public ITaskRoutine<T> SetEnumeratorProvider(Func<T> taskGenerator)
        {
            _taskEnumerator = default(T);
            _taskGenerator = taskGenerator;

            return this;
        }
        
        public ITaskRoutine<T> SetEnumeratorRef(ref T taskEnumerator)
        {
            _taskGenerator = null;
            if (_isValueType || (IEnumerator)_taskEnumerator != (IEnumerator)taskEnumerator)
                _taskEnumeratorJustSet = true;
            _taskEnumerator = taskEnumerator;
#if DEBUG && !PROFILER
            _compilerGenerated = taskEnumerator.GetType().IsCompilerGenerated();
#else
            _compilerGenerated = false;
#endif
            return this;
        }

        public ITaskRoutine<T> SetEnumerator(T taskEnumerator)
        {
            return SetEnumeratorRef(ref taskEnumerator);
        }

        public void Pause()
        {
            _paused = true;
            ThreadUtility.MemoryBarrier();
        }

        public void Resume()
        {
            _paused = false;
            ThreadUtility.MemoryBarrier();
        }

        public void Stop()
        {
            _explicitlyStopped = true;

            if (onExplicitlyStopped != null)
            {
                onExplicitlyStopped();
                onExplicitlyStopped = null;
            }

            ThreadUtility.MemoryBarrier();
        }

        public bool isRunning
        {
            get
            {
                return _started == true && _completed == false;
            }
        }

        public ContinuationWrapper Start(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            _onStop = onStop;
            _onFail = onFail;
            
            InternalStart();

            return _continuationWrapper;
        }

        public object Current
        {
            get
            {
                if (_coroutine != null)
                    return _coroutine.Current;

                return null;
            }
        }

        public override string ToString()
        {
            if (_name == string.Empty)
            {
                if (_taskGenerator == null && isTaskEnumeratorValid == false)
                    _name = base.ToString();
                else
                if (isTaskEnumeratorValid == true)
                    _name = _taskEnumerator.ToString();
                else
                {
                    var methodInfo = _taskGenerator.GetMethodInfoEx();
                    
                    _name = methodInfo.GetDeclaringType().ToString().FastConcat(".", methodInfo.Name);
                }
            }

            return _name;
        }
        
        public bool isTaskEnumeratorValid
        {
            get { return _isValueType || _taskEnumerator != null; }
        }

        /// <summary>
        /// Move Next is called by the current runner, which could be on another thread!
        /// that means that the --->class states used in this function must be thread safe<-----
        /// </summary>
        /// <returns></returns>
        public bool MoveNext()
        {
#if DEBUG
            if (_started == false)
            {
                throw new Exception("A taskRoutine cannot be yielded directly, must be yielded through the Start() function");
            }
#endif                
            ///
            /// Stop() can be called from whatever thread, but the 
            /// runner won't know about it until the next MoveNext()
            /// is called. It's VERY important that a task is not reused
            /// until naturally stopped through this mechanism, otherwise
            /// there is the risk to add the same task twice in the 
            /// runner queue. The new task must be added in the queue
            /// through the pending enumerator functionality
            /// 
            /// DO NOT USE FUNCTIONS AS IT MUST BE CLEAR WHICH STATES ARE USED
            /// 
            /// threadsafe states:
            /// - _explicitlyStopped
            /// - _completed
            /// - _paused
            /// - _runner
            /// - _pool
            /// - _pendingRestart
            /// - _started
            /// 
            ThreadUtility.MemoryBarrier();
            if (_explicitlyStopped == true || _runner.isStopping == true)
            {
                _completed = true;
                
                ThreadUtility.MemoryBarrier();

                if (_onStop != null)
                    _onStop();
            }
            else    
            if (_runner.paused == false && _paused == false)
            {
                try
                {
                    DBC.Tasks.Check.Assert(_started == true, _callStartFirstError);
                    
                    _completed = !_coroutine.MoveNext();
                    ThreadUtility.MemoryBarrier();
                    
                    var current = _coroutine.Current;
                    if (current == Break.It ||
                        current == Break.AndStop)
                    {
                        if (_onStop != null)
                            _onStop();
                    }
                }
                catch (Exception e)
                {
                    _completed = true;
                    ThreadUtility.MemoryBarrier();
                    
                    if (_onFail != null && (e is TaskYieldsIEnumerableException) == false)
                        _onFail(new PausableTaskException(e));
                    else
                    {
                       Utility.Console.LogException(e);
                    }
#if DEBUG
                    throw;
#endif
                }
            }

            if (_completed == true)
            {
                if (_pool != null)
                {
                    DBC.Tasks.Check.Assert(_pendingRestart == false, "a pooled task cannot have a pending restart!");
                    
                    _pool.PushTaskBack(this as PausableTask<IEnumerator>);
                }
                else
                //TaskRoutine case only!! This is the most risky part of this code
                //when the code enters here, another thread could be about to
                //set a pending restart!
                {
                    ThreadUtility.MemoryBarrier();
                    if (_pendingRestart == true)
                    {
                        _pendingContinuationWrapper.Completed();
                        
                        //start new coroutine using this task
                        //this will put _started to true (it was already though)
                        //it uses the current runner to start the pending task
                        Restart(_pendingEnumerator);
                    }
                    else
                    {
                        _continuationWrapper.Completed();
                    }
                }

                ThreadUtility.MemoryBarrier();

                return false;
            }

            return true;
        }

        /// <summary>
        /// Reset task on reuse, when fetched from the Pool
        /// </summary>
        public void Reset()
        {
            CleanUpOnRecycle();

            _paused = false;
            _taskEnumeratorJustSet = false;
            _completed = false;
            _started = false;
            _explicitlyStopped = false;
            _compilerGenerated = false;
            _pendingRestart = false;
            _name = string.Empty;
        }

        /// <summary>
        /// Clean up task on complete
        /// This function doesn't need to
        /// reset any state, is only to
        /// release resources!!!
        /// </summary>
        internal void CleanUpOnRecycle()
        {
            _pendingEnumerator = default(T);
            _taskGenerator = null;
            _taskEnumerator = default(T);
            _runner = null;
            _onFail = null;
            _onStop = null;

            _coroutineWrapper.Clear();
        }

        /// <summary>
        /// Clean up task on Restart 
        /// can happen only through ITaskRoutine
        /// </summary>
        void CleanUpOnRestart()
        {
            DBC.Tasks.Check.Require(_pool == null, "CleanUpOnRestart can run only on not pooled TaskRoutine");
            
            _paused = false;
            _taskEnumeratorJustSet = false;
            _completed = false;
            _explicitlyStopped = false;
            _pendingRestart = false;
            _name = string.Empty;
            
            _pendingEnumerator = default(T);
            _pendingContinuationWrapper = null;
            
            _coroutineWrapper.Clear();
            
            if (_continuationWrapper != null)
                _continuationWrapper.Reset();
        }

        internal PausableTask(PausableTaskPool pool)
        {
            _coroutineWrapper = new SerialTaskCollection<T, object>(1);
            _pool = pool;
            
            Reset();
        }

        internal PausableTask()
        {
            _coroutineWrapper = new SerialTaskCollection<T, object>(1);
            _continuationWrapper = new ContinuationWrapper();

            Reset();
        }

        /// <summary>
        /// A Pausable Task cannot be recycled from the pool if hasn't been
        /// previously completed.
        /// A task can actually be restarted, but this will stop the previous
        /// enumeration, even if the enumerator didn't change.
        /// However since an enumerator can be enumerated on another runner
        /// a task cannot set as completed immediatly, but it must wait for
        /// the next MoveNext. This is what the Pending logic is about.
        /// </summary>
        /// <param name="task"></param>
        void InternalStart()
        {
            DBC.Tasks.Check.Require(_pendingRestart == false, "a task has been reused while is pending to start");
            DBC.Tasks.Check.Require(_taskGenerator != null || isTaskEnumeratorValid == true , "An enumerator or enumerator provider is required to enable this function, please use SetEnumeratorProvider/SetEnumerator before to call start");
            
            Resume(); //if it's paused, must resume
            
            var originalEnumerator = _taskEnumerator;
                
            if (_taskGenerator != null)
                originalEnumerator = _taskGenerator();
            
            //TaskRoutine case only!!
            ThreadUtility.MemoryBarrier();
            if (_pool == null 
                && _completed == false 
                && _started == true
                && _explicitlyStopped == true)
            {
                _pendingEnumerator = originalEnumerator;
                _pendingContinuationWrapper = _continuationWrapper;
                _pendingRestart = true;
                
                _continuationWrapper = new ContinuationWrapper();
                
                ThreadUtility.MemoryBarrier();

                return;
            }
            
            Restart(originalEnumerator);
        }

        void Restart(T task)
        {
            DBC.Tasks.Check.Require(_runner != null, "SetScheduler function has never been called");
            
            if (isTaskEnumeratorValid == true  && _taskEnumeratorJustSet == false)
            {
                DBC.Tasks.Check.Assert(_compilerGenerated == false, "Cannot restart an IEnumerator without a valid Reset function, use SetEnumeratorProvider instead ".FastConcat(_name));
                
                task.Reset();
            }
            
            CleanUpOnRestart();
            SetTask(task);

            _started = true;
            ThreadUtility.MemoryBarrier();

            _runner.StartCoroutine(this);
        }

        void SetTask(T task)
        {
            if (task is ITaskCollection<T> == false)
            {
                _coroutineWrapper.Clear();
                _coroutineWrapper.Add(task);
                _coroutine = _coroutineWrapper;
            }
            else
                _coroutine = task as ITaskCollection<T>;
#if DEBUG && !PROFILER            
            _callStartFirstError = CALL_START_FIRST_ERROR.FastConcat(" task: ", ToString());
#endif            
        }

        IRunner                        _runner;
        ITaskCollection<T>             _coroutine;

        readonly SerialTaskCollection<T, object> _coroutineWrapper;
        
        ContinuationWrapper           _continuationWrapper;
        ContinuationWrapper           _pendingContinuationWrapper;
        
        bool                          _compilerGenerated;
        bool                          _taskEnumeratorJustSet;

        T                             _pendingEnumerator;
        T                             _taskEnumerator;
      
        readonly PausableTaskPool     _pool;
        Func<T>                       _taskGenerator;

        Action<PausableTaskException> _onFail;
        Action                        _onStop;
        string                        _name = String.Empty;
        
        bool                          _started;

        volatile bool                 _completed;
        volatile bool                 _explicitlyStopped;
        volatile bool                 _paused;
        volatile bool                 _pendingRestart;
        
        string                        _callStartFirstError = string.Empty;
    

        static readonly bool                _isValueType      = typeof(T).IsValueTypeEx();
    }
}

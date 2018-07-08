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
using System.Collections.Generic;
using Svelto.Tasks.Internal;
using Unity.Collections;

#if DEBUG && !PROFILER
using System.Runtime.CompilerServices;
#endif    

namespace Svelto.Tasks
{
    public class PausableTaskException : Exception
    {
        public PausableTaskException(Exception e)
            : base(e.ToString(), e)
        { }
    }

    public class ContinuationWrapper<T>:IEnumerator<T> where T:IEnumerator
    {
        public ContinuationWrapper(PausableTask<T> pausableTask)
        {
            _pausableTask = pausableTask;
        }
        
        public bool MoveNext()
        {
            ThreadUtility.MemoryBarrier();
            
            return _pausableTask.isRunning;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public T Current
        {
            get { return _pausableTask.Current; }
        }

        object IEnumerator.Current { get { return null; } }

        readonly PausableTask<T> _pausableTask;

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}

namespace Svelto.Tasks
{
    public struct TaskRoutine<T> : ITaskRoutine<T> where T:IEnumerator
    {
        internal TaskRoutine(PausableTask<T> pausableTask)
        {
            _pausableTask = pausableTask;
        }
        
        public TaskRoutine<T> SetEnumeratorProvider(Func<T> taskGenerator)
        {
            return _pausableTask.SetEnumeratorProvider(taskGenerator);
        }

        public TaskRoutine<T> SetEnumerator(T taskGenerator)
        {
            return _pausableTask.SetEnumerator(taskGenerator);
        }

        public TaskRoutine<T> SetEnumeratorRef(ref T taskGenerator)
        {
            return _pausableTask.SetEnumeratorRef(ref taskGenerator);
        }

        public ContinuationWrapper<T> Start(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            return (_pausableTask as ITaskRoutine<T>).Start(onFail, onStop);
        }

        public void Pause()
        {
            _pausableTask.Pause();
        }

        public void Resume()
        {
            _pausableTask.Resume();
        }

        public void Stop()
        {
            _pausableTask.Stop();
        }

        public bool isRunning
        {
            get { return _pausableTask.isRunning; }
        }
        
        readonly PausableTask<T> _pausableTask;
    }

    public class PausableTask : PausableTask<IEnumerator>
    {
        internal PausableTask(PausableTaskPool pool):base()
        {
            _pool             = pool;
        }
        
        /// <summary>
        /// Reset task on reuse, when fetched from the Pool
        /// </summary>
        public new void Reset()
        {
            base.Recycle();
        }

        protected override void WaitToComplete(ref IEnumerator originalEnumerator)
        {}
        
        internal void Start()
        {
            InternalStart();
        }
        
        internal void SetRunner(IRunner<IEnumerator> runner)
        {
            _runner = runner;
        }

        protected override bool MustMoveNext()
        {
            _pool.PushTaskBack(this);

            return true;
        }

        readonly PausableTaskPool _pool;

    }

    public class PausableTask<T> : IEnumerator<TaskCollection<T>.CollectionTask>, IEnumerator<T>, ITaskRoutine<T> where T:IEnumerator
    {
#if DEBUG && !PROFILER        
        const string CALL_START_FIRST_ERROR = "Enumerating TaskRoutine without starting it, please call Start() first";
#endif
        internal Action onExplicitlyStopped { private get; set; }

        /// <summary>
        /// Calling SetScheduler, SetEnumeratorProvider, SetEnumerator
        /// on a running task won't stop the task until either 
        /// Stop() or Start() is called.
        /// </summary>
        /// <param name="runner"></param>
        /// <returns></returns>
        public TaskRoutine<T> SetEnumeratorProvider(Func<T> taskGenerator)
        {
            _taskEnumerator = default(T);
            _taskGenerator = taskGenerator;

            return new TaskRoutine<T>(this);
        }
        
        public TaskRoutine<T> SetEnumeratorRef(ref T taskEnumerator)
        {
            _taskGenerator = null;
            if (!(default(T) == null && (IEnumerator)_taskEnumerator == (IEnumerator)taskEnumerator))
                _state.taskEnumeratorJustSet = true;

            _taskEnumerator = taskEnumerator;
            
            return new TaskRoutine<T>(this);
        }

        public TaskRoutine<T> SetEnumerator(T taskEnumerator)
        {
            return SetEnumeratorRef(ref taskEnumerator);
        }
        
        public void Pause()
        {
            _state.paused = true;
            ThreadUtility.MemoryBarrier();
        }

        public void Resume()
        {
            _state.paused = false;
            ThreadUtility.MemoryBarrier();
        }

        public void Stop()
        {
            _state.explicitlyStopped = true;

            if (onExplicitlyStopped != null)
            {
                onExplicitlyStopped();
                onExplicitlyStopped = null;
            }

            ThreadUtility.MemoryBarrier();
        }

        public bool isRunning
        {
            get { return _state.isRunning; }
        }

        ContinuationWrapper<T> ITaskRoutine<T>.Start(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            _onStop = onStop;
            _onFail = onFail;
            
            InternalStart();

            return _continuationWrapper;
        }
        
        object IEnumerator.Current
        {
            get
            {
                return ((IEnumerator<TaskCollection<T>.CollectionTask>)_coroutine).Current;
            }
        }

        TaskCollection<T>.CollectionTask IEnumerator<TaskCollection<T>.CollectionTask>.Current
        {
            get { throw new Exception(); }
        }

#if DEBUG        
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
#endif
        public void Dispose()
        {}

        bool isTaskEnumeratorValid
        {
            get { if (default(T) == null)
                    return _taskEnumerator != null;
                return true;
            }
        }

        /// <summary>
        /// Move Next is called by the current runner, which could be on another thread!
        /// that means that the --->class states used in this function must be thread safe<-----
        /// </summary>
        /// <returns></returns>
        public bool MoveNext()
        {
#if DEBUG
            if (_state.started == false)
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
            if (_state.explicitlyStopped == true || _runner.isStopping == true)
            {
                _state.completed = true;
                
                ThreadUtility.MemoryBarrier();

                if (_onStop != null)
                    _onStop();
            }
            else    
            if (_runner.paused == false && _state.paused == false)
            {
                try
                {
#if DEBUG && !PROFILER                    
                    DBC.Tasks.Check.Assert(_state.started == true, _callStartFirstError);
#endif    
                    _state.completed = !_coroutine.MoveNext();
                    ThreadUtility.MemoryBarrier();
                    
                    var current = (_coroutine as IEnumerator<TaskCollection<T>.CollectionTask>).Current.breakIt;
                    if (current == Break.It ||
                        current == Break.AndStop)
                    {
                        if (_onStop != null)
                            _onStop();
                    }
                }
                catch (Exception e)
                {
                    _state.completed = true;
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

            if (_state.completed == true)
            {
                return MustMoveNext();
            }

            return true;
        }

        protected virtual bool MustMoveNext()
        {
            return false;
        }

        /// <summary>
        /// Reset task on reuse, when fetched from the Pool
        /// </summary>
        public void Reset()
        {
            throw new NotImplementedException();
        }

        public T Current
        {
            get { return (_coroutine as IEnumerator<T>).Current; }
        }

        protected void Recycle()
        {
            _state = new State();
#if DEBUG        
            _name              = string.Empty;
#endif            
            _taskGenerator     = null;
            _taskEnumerator    = default(T);
            _runner            = null;
            _onFail            = null;
            _onStop            = null;

            _coroutineWrapper.Clear();
        }

        void CleanUpOnRestart()
        {
            _state = new State();
#if DEBUG            
            _name = string.Empty;
#endif            
            _coroutineWrapper.Clear();
        }

        internal PausableTask()
        {
            _coroutineWrapper = new SerialTaskCollection<T>(1);
            _continuationWrapper = new ContinuationWrapper<T>(this);
        }

        internal PausableTask(IRunner<T> runner) : this()
        {
            _runner = runner;
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
        protected void InternalStart()
        {
            DBC.Tasks.Check.Require(_taskGenerator != null || isTaskEnumeratorValid == true , "An enumerator or enumerator provider is required to enable this function, please use SetEnumeratorProvider/SetEnumerator before to call start");
            
            Resume(); //if it's paused, must resume
            
            var originalEnumerator = _taskEnumerator;
                
            if (_taskGenerator != null)
                originalEnumerator = _taskGenerator();
            
            WaitToComplete(ref originalEnumerator);
            
            Restart(originalEnumerator);
        }

        protected virtual void WaitToComplete(ref T originalEnumerator)
        {
            ThreadUtility.MemoryBarrier();
            
            if (_state.completed == false 
             && _state.started == true
             && (_state.explicitlyStopped == true || _state.taskEnumeratorJustSet == true))
            {
    //            _runner.StartCoroutine(new PausableTask<T>(this));
            }            
        }

        void Restart(T task)
        {
            DBC.Tasks.Check.Require(_runner != null, "SetScheduler function has never been called");
            
            if (isTaskEnumeratorValid == true  && _state.taskEnumeratorJustSet == false)
            {
#if DEBUG && !PROFILER
                DBC.Tasks.Check.Assert(_taskEnumerator.GetType().IsCompilerGenerated() == false, "Cannot restart an IEnumerator without a valid Reset function, use SetEnumeratorProvider instead ".FastConcat(_name));
#endif   
                task.Reset();
            }
            
            CleanUpOnRestart();
            SetTask(task);

            _state.started = true;
            ThreadUtility.MemoryBarrier();

            _runner.StartCoroutine(this);
        }

        void SetTask(T task)
        {
            if (task is ITaskCollection<T> == false)
            {
                _coroutineWrapper.Add(task);
                _coroutine = _coroutineWrapper;
            }
            else
                _coroutine = task as ITaskCollection<T>;
#if DEBUG && !PROFILER            
            _callStartFirstError = CALL_START_FIRST_ERROR.FastConcat(" task: ", ToString());
#endif            
        }
        
        protected IRunner<T>          _runner;
        
        readonly SerialTaskCollection<T> _coroutineWrapper;
        readonly ContinuationWrapper<T>  _continuationWrapper;
        
        ITaskCollection<T>            _coroutine; //todo: cosa succede se passo multithreadparallelcollection qui?

        T                             _taskEnumerator;
        Func<T>                       _taskGenerator;

        Action<PausableTaskException> _onFail;
        Action                        _onStop;
        
        State                         _state;
#if DEBUG        
        string                        _name = String.Empty;
        string                        _callStartFirstError;
#endif
        
        struct State
        {
            //maybe the only way to have flags threadsafe
            //is to use the SafeBitVector32 struct (to get through dotpeek)
            public volatile bool completed;
            public volatile bool explicitlyStopped;
            public volatile bool paused;
            public volatile bool started;
            public volatile bool taskEnumeratorJustSet;
            
            public bool isRunning { get { return started == true && completed == false;} }
        }
    }
}

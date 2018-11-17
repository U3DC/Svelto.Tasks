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

    public struct ContinuationWrapper<T>:IEnumerator<T> where T:IEnumerator
    {
        public ContinuationWrapper(SveltoTask<T> pausableTask)
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

        readonly SveltoTask<T> _pausableTask;

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}

namespace Svelto.Tasks
{
    public class SveltoTask : SveltoTask<IEnumerator>
    {
        internal SveltoTask(PausableTaskPool pool):base()
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

        internal void Start()
        {
            InternalStart();
        }
        
        public void SetScheduler(IRunner<IEnumerator> runner)
        {
            _runner = runner;
        }

        protected override bool OnComplete()
        {
            _pool.PushTaskBack(this);

            return true;
        }

        readonly PausableTaskPool _pool;

    }

    public class SveltoTask<T> : IEnumerator<TaskCollection<T>.CollectionTask>, IEnumerator<T> where T:IEnumerator
    {
#if DEBUG && !PROFILER        
        const string CALL_START_FIRST_ERROR = "Enumerating TaskRoutine without starting it, please call Start() first";
#endif
        internal event Action onTaskHasBeenInterrupted;

        /// <summary>
        /// Calling SetScheduler, SetEnumeratorProvider, SetEnumerator
        /// on a running task won't stop the task until either 
        /// Stop() or Start() is called.
        /// </summary>
        /// <param name="runner"></param>
        /// <returns></returns>
        /// 
        public void SetScheduler(IRunner<T> runner)
        {
            _runner = runner;
        }
         
        public void SetEnumeratorProvider(Func<T> taskGenerator)
        {
            _taskEnumerator = default(T);
            _taskGenerator = taskGenerator;
        }
        
        public void SetEnumerator(T taskEnumerator)
        {
            _taskGenerator = null;
            if (!(default(T) == null && (IEnumerator)_taskEnumerator == (IEnumerator)taskEnumerator))
                _state.taskEnumeratorJustSet = true;

            _taskEnumerator = taskEnumerator;
        }
        
        public void Pause()
        {
            _state.paused = true;
        }

        public void Resume()
        {
            _state.paused = false;
        }

        public void Stop()
        {
            _state.explicitlyStopped = true;

            OnTaskInterrupted();
        }
        
        void OnTaskInterrupted()
        {
            if (onTaskHasBeenInterrupted != null)
            {
                onTaskHasBeenInterrupted.Invoke();
                ClearInvokes();
            }
        }
        
        void ClearInvokes()
        {
            if (onTaskHasBeenInterrupted == null) return;
            foreach (var d in onTaskHasBeenInterrupted.GetInvocationList())
            {
                onTaskHasBeenInterrupted -= (Action) d;
            }
        }

        public bool isRunning
        {
            get { return _state.isRunning; }
        }

        public ContinuationWrapper<T> StartRoutine(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            _state.syncPoint = true;
            
            _onStop = onStop;
            _onFail = onFail;
            
            OnTaskInterrupted();
            
            InternalStart();
            _state.syncPoint = false;

            return _continuationWrapper;
        }
        
        public void Start(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            _onStop = onStop;
            _onFail = onFail;
            
            InternalStart();
        }
        
        object IEnumerator.Current
        {
            get
            {
                var enumerator = ((IEnumerator<TaskCollection<T>.CollectionTask>)_coroutine);
                if (enumerator != null)
                    return enumerator.Current;

                return null;
            }
        }

        TaskCollection<T>.CollectionTask IEnumerator<TaskCollection<T>.CollectionTask>.Current
        {
            get { throw new Exception(); }
        }

       
        public override string ToString()
        {
#if DEBUG && !PROFILER
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
#endif
            return _name;
        }

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
            if (_state.completed == false && _state.syncPoint == false)
            {
                if (_state.explicitlyStopped == true || _runner.isStopping == true)
                {
                    _state.completed = true;

                    if (_onStop != null)
                        _onStop();
                }
                else if (_runner.paused == false && _state.paused == false)
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

                        if (_onFail != null && (e is TaskYieldsIEnumerableException) == false)
                            _onFail(new PausableTaskException(e));
                        else
                        {
                            Utilities.Console.LogError("Svelto.Tasks task threw an exception: ", ToString());
                            Utilities.Console.LogException(e);
                        }
#if DEBUG
                        throw;
#endif
                    }
                }
            }

            if (_state.completed == true && _state.syncPoint == false)
            {
                return OnComplete();
            }

            return true;
        }

        protected virtual bool OnComplete()
        {
            if (_state.pendingRestart)
            {
                Restart(_taskGenerator != null ? _taskGenerator() : _taskEnumerator);

                return true;
            }

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

        internal SveltoTask()
        {
            _coroutineWrapper = new SerialTaskCollection<T>(1);
            _continuationWrapper = new ContinuationWrapper<T>(this);
        }

        internal SveltoTask(IRunner<T> runner) : this()
        {
            _runner = runner;
        }

        /// <summary>
        /// A SveltoTask cannot be recycled from the pool if hasn't been previously completed.
        /// A task can actually be restarted, but this will stop the previous enumeration, even if the enumerator didn't
        /// change.
        /// However since an enumerator can be enumerated on another runner a task cannot set as completed immediately,
        /// but it must wait for the next MoveNext. This is what the Pending logic is about.
        /// </summary>
        /// <param name="task"></param>
        protected void InternalStart()
        {
            DBC.Tasks.Check.Require(_taskGenerator != null || isTaskEnumeratorValid == true , "An enumerator or enumerator provider is required to enable this function, please use SetEnumeratorProvider/SetEnumerator before to call start");
            
            Resume(); //if it's paused, must resume

            if (IsTaskStillEnumerating() == false)
                Restart(_taskGenerator != null ? _taskGenerator() : _taskEnumerator);
        }

        bool IsTaskStillEnumerating()
        {
            ThreadUtility.MemoryBarrier();

            return _state.isRunningAnOldTask;
        }

        void Restart(T task)
        {
            DBC.Tasks.Check.Require(_runner != null, "SetScheduler function has never been called");
            
            if (isTaskEnumeratorValid == true  && _state.taskEnumeratorJustSet == false)
            {
#if DEBUG && !PROFILER
                DBC.Tasks.Check.Assert(IsCompilerGenerated<T>.isCompilerGenerated == false, "Cannot restart an IEnumerator without a valid Reset function, use SetEnumeratorProvider instead ".FastConcat(_name));
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
            volatile byte _value;
            
            const byte COMPLETED_BIT = 0;
            const byte STARTED_BIT = 3;
            const byte EXPLICITLY_STOPPED = 1;
            const byte TASK_ENUMERATOR_JUST_SET = 4;
            
            public bool completed             { get { return BIT(COMPLETED_BIT); } set { if (value) SETBIT(COMPLETED_BIT); else UNSETBIT(COMPLETED_BIT); } }
            public bool explicitlyStopped     { get { return BIT(EXPLICITLY_STOPPED); } set { if (value) SETBIT(EXPLICITLY_STOPPED); else UNSETBIT(EXPLICITLY_STOPPED); } }
            public bool paused                { get { return BIT(2); } set { if (value) SETBIT(2); else UNSETBIT(2); } }
            public bool started               { get { return BIT(STARTED_BIT); } set { if (value) SETBIT(STARTED_BIT); else UNSETBIT(STARTED_BIT); } }
            public bool taskEnumeratorJustSet { get { return BIT(TASK_ENUMERATOR_JUST_SET); } set { if (value) SETBIT(TASK_ENUMERATOR_JUST_SET); else UNSETBIT(TASK_ENUMERATOR_JUST_SET); } }
            public bool syncPoint             { get { return BIT(5); } set { if (value) SETBIT(5); else UNSETBIT(5); } }
            public bool pendingRestart        { get { return BIT(6); } set { if (value) SETBIT(6); else UNSETBIT(6); } }

            void SETBIT(byte index)
            {
                ThreadUtility.VolatileWrite(ref _value, (byte) (_value | 1 << index));
            }
            
            void UNSETBIT(int index)
            {
                ThreadUtility.VolatileWrite(ref _value, (byte) (_value &  ~(1 << index)));
            }

            bool BIT(byte index)
            {
                var shbit = SHBIT(index);
                return (ThreadUtility.VolatileRead(ref _value) & shbit) == shbit;
            }

            static byte SHBIT(byte shift)
            {
                return (byte) (1 << shift);
            }

            public bool isRunning { get
            {
                var started = SHBIT(STARTED_BIT);
                var completedAndStarted = started | SHBIT(COMPLETED_BIT);
                
                return (ThreadUtility.VolatileRead(ref _value) & completedAndStarted) == started; //started but not completed
            } }
            public bool isRunningAnOldTask { get
            {
                var startedAndMustRestart = SHBIT(STARTED_BIT) | SHBIT(EXPLICITLY_STOPPED) | SHBIT(TASK_ENUMERATOR_JUST_SET);
                var completedAndStartedAndMustRestart    = startedAndMustRestart | SHBIT(COMPLETED_BIT);
                
                return (ThreadUtility.VolatileRead(ref _value) & completedAndStartedAndMustRestart) == startedAndMustRestart; //it's set to restart, but not completed
            } }
        }
    }
}

using System;
using System.Collections;
using Svelto.Utilities;

namespace Svelto.Tasks
{
    public class PausableTaskException : Exception
    {
        public PausableTaskException(Exception e)
            : base(e.ToString(), e)
        { }
        
        public PausableTaskException(string message, Exception e)
            : base(message.FastConcat(" -", e.ToString()), e)
        { }
    }

    public interface IPausableTask:IEnumerator
    {}
    
    public struct ContinuationWrapper<T> : IEnumerator where T:IEnumerator
    {
        public ContinuationWrapper(SveltoTask<T> pausableTask)
        {
            _pausableTask = pausableTask;
        }
        
        public bool MoveNext()
        {
            return _pausableTask.isRunning;
        }

        public void Reset()
        {}

        public object Current
        {
            get { return _pausableTask.Current; }
        }

        public bool completed
        {
            get { return _pausableTask.isRunning == false; } //todo these shouldn't use volatile
        }

        readonly SveltoTask<T> _pausableTask;
    }

    public struct SveltoTask<T>: IPausableTask where T: IEnumerator 
    {
        internal event Action onTaskHasBeenInterrupted;
        
        T _enumerator;
        
        /// <summary>
        /// Calling SetScheduler, SetEnumeratorProvider, SetEnumerator
        /// on a running task won't stop the task until either 
        /// Stop() or Start() is called.
        /// </summary>
        /// <param name="runner"></param>
        /// <returns></returns>
        public SveltoTask(IRunner<T> runner):this()
        {
            _runner = runner;
        }

        public void SetEnumeratorProvider(Func<T> taskGenerator)
        {
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

        public bool isRunning
        {
            get { return _state.isRunning; }
        }

        public ContinuationWrapper<T> Start(Action<PausableTaskException> onFail = null, Action onStop = null)
        {
            _onStop = onStop;
            _onFail = onFail;
            
            OnTaskInterrupted();
            
            InternalStart();
            
            return new ContinuationWrapper<T>(this);
        }

        public object Current
        {
            get
            {
                throw new NotImplementedException();
            }
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
            
            return _name;
#endif
            return "Stripped in Release";
        }

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
            /// - _started
            ///
            /// 
            try
            {
                if (_state.completed == false && _state.syncPoint == false) //todo: one condition
                {
                    if (_state.explicitlyStopped == true || _runner.isStopping == true)
                    {
                        _state.completed = true;

                        if (_onStop != null)
                        {
                            try
                            {
                                _onStop();
                            }
                            catch (Exception onStopException)
                            {
                                Utilities.Console.LogError("Svelto.Tasks task OnStop callback threw an exception ", ToString());
                                Utilities.Console.LogException(onStopException);
                            }
                        }
                    }
                    else
                        if (_runner.paused == false && _state.paused == false)
                        {
#if DEBUG && !PROFILER
                            DBC.Tasks.Check.Assert(_started == true, _callStartFirstError);
#endif
                            try
                            {
                                _state.completed = !_coroutine.MoveNext();
                                
                                var current = _coroutine.Current.current;
                                if ((current == Break.It || current == Break.AndStop) && _onStop != null)
                                {
                                    try
                                    {
                                        _onStop();
                                    }
                                    catch (Exception onStopException)
                                    {
                                        Utilities.Console.LogError("Svelto.Tasks task OnStop callback threw an exception ", ToString());
                                        Utilities.Console.LogException(onStopException);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                _state.completed = true;

                                if (_onFail != null && (e is TaskYieldsIEnumerableException) == false)
                                {
                                    try
                                    {
                                        _onFail(new PausableTaskException(e));
                                    }
                                    catch (Exception onFailException)
                                    {
                                        Utilities.Console.LogError("Svelto.Tasks task OnFail callback threw an exception ", ToString());
                                        Utilities.Console.LogException(onFailException);
                                    }
                                }
                                else
                                {
                                    Utilities.Console.LogError("a Svelto.Tasks task threw an exception: ", ToString());
                                    Utilities.Console.LogException(e);
                                }
                            }
                        }
                }

                if (_state.completed == true && _state.syncPoint == false)
                    return false;

                return true;
            }
            catch (Exception e)
            {
                Utilities.Console.LogException(new PausableTaskException("Something went drastically wrong inside a PausableTask", e));

                throw;
            }
        }

        /// <summary>
        /// Reset task on reuse, when fetched from the Pool
        /// </summary>
        public void Reset()
        {
            _taskGenerator  = null;
            _runner         = null;
            _onFail         = null;
            _onStop         = null;

            ClearInvokes();

            _state.paused = false;
            _state.taskEnumeratorJustSet = false;
            _state.completed = false;
            _state.started = false;
            _state.explicitlyStopped = false;

            _state.pendingRestart = false;
#if DEBUG && !PROFILER            
            _name = string.Empty;
#endif
        }
        
        /// <summary>
        /// Clean up task on Restart can happen only through ITaskRoutine when they restart
        /// </summary>
        void CleanUpOnRestart()
        {
            _state.paused = false;
            _state.taskEnumeratorJustSet = false;
            _state.completed = false;
            _state.explicitlyStopped = false;
            _state.pendingRestart = false;
            
            ClearInvokes();
        }
        
        /// <summary>
        /// A PausableTask cannot be recycled from the pool if hasn't been previously completed.
        /// A task can actually be restarted, but this will stop the previous enumeration, even if the enumerator didn't
        /// change.
        /// However since an enumerator can be enumerated on another runner a task cannot set as completed immediately,
        /// but it must wait for the next MoveNext. This is what the Pending logic is about.
        /// </summary>
        /// <param name="task"></param>
        void InternalStart()
        {
            DBC.Tasks.Check.Require(_taskGenerator != null || isTaskEnumeratorValid == true , "An enumerator or enumerator provider is required to enable this function, please use SetEnumeratorProvider/SetEnumerator before to call start");
            
            _state.syncPoint = true;

            Resume(); //if it's paused, must resume

            if (IsTaskStillEnumerating() == false)
            {
                var task = _taskGenerator != null ? _taskGenerator() : _taskEnumerator;
                DBC.Tasks.Check.Require(_runner != null, "SetScheduler function has never been called");
            
                if (isTaskEnumeratorValid == true  && _state.taskEnumeratorJustSet == false)
                {
#if DEBUG && !PROFILER                                
                    DBC.Tasks.Check.Assert(IsCompilerGenerated<T>.isCompilerGenerated == false, "Cannot restart an IEnumerator without a valid Reset function, use SetEnumeratorProvider instead ".FastConcat(_name));
#endif    

                    task.Reset();
                }
            
                CleanUpOnRestart();

                _state.started   = true;
                _state.syncPoint = false; //very important this to happen now

                _coroutine = _runner.PrepareTask(task);
                _coroutine.Add(task);
                _runner.StartCoroutine(this);
            }
            else            
                _state.syncPoint = false;
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
        
        bool IsTaskStillEnumerating()
        {
            return _state.isRunningAnOldTask;
        }

        IRunner<T>               _runner;

        T       _taskEnumerator;
        Func<T> _taskGenerator;

        Action<PausableTaskException> _onFail;
        Action                        _onStop;
        
        State _state;
        SerialTaskCollection<T> _coroutine;

#if DEBUG && !PROFILER        
        string _name = String.Empty; string _callStartFirstError; 
        const string CALL_START_FIRST_ERROR = "Enumerating PausableTask without starting it, please call Start() first";
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

            public bool isRunning 
            { 
                get
                {
                    var started = SHBIT(STARTED_BIT);
                    var completedAndStarted = started | SHBIT(COMPLETED_BIT);
                    
                    return (ThreadUtility.VolatileRead(ref _value) & completedAndStarted) == started; //started but not completed
                } 
            }
            
            public bool isRunningAnOldTask 
            { 
                get
                {
                    var startedAndMustRestart = SHBIT(STARTED_BIT) | SHBIT(EXPLICITLY_STOPPED) | SHBIT(TASK_ENUMERATOR_JUST_SET);
                    var completedAndStartedAndMustRestart    = startedAndMustRestart | SHBIT(COMPLETED_BIT);
                    
                    return (ThreadUtility.VolatileRead(ref _value) & completedAndStartedAndMustRestart) == startedAndMustRestart; //it's set to restart, but not completed
                } 
            }
        }
    }
}

//test restart
//test continuator

//fase uno rimuovere pending
//fase due dividere taskroutine da non task routine
//fase tre wrappers per struct and funzioni

///
/// Unit tests to write:
/// Restart a task with compiled generated IEnumerator
/// Restart a task with IEnumerator class
/// Restart a task after SetEnumerator has been called (this must be still coded, as it must reset some values)
/// Restart a task just restarted (pendingRestart == true)
/// Start a taskroutine twice with different compiler generated enumerators and variants
/// 
/// 

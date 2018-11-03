using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Svelto.DataStructures;
using Svelto.Tasks.Unity.Internal;
using Svelto.Utilities;

#if NETFX_CORE
using System.Threading.Tasks;
#endif

namespace Svelto.Tasks
{
    //The multithread runner always uses just one thread to run all the couroutines
    //If you want to use a separate thread, you will need to create another MultiThreadRunner
    
    public class MultiThreadRunner : MultiThreadRunner<IEnumerator>, IRunner
    {
        public MultiThreadRunner(string name, int intervalInMS) : base(name, intervalInMS)
        {}
        
        public MultiThreadRunner(string name) : base(name)
        {}
    }
        
    public class MultiThreadRunner<T> : IRunner<T> where T:IEnumerator
    {
        public bool paused { set; get; }

        public bool isStopping
        {
            get
            {
                ThreadUtility.MemoryBarrier();
                return _runnerData._waitForFlush;
            }
        }

        public int numberOfRunningTasks
        {
            get { return _runnerData.Count; }
        }

        public override string ToString()
        {
            return _name;
        }

        ~MultiThreadRunner()
        {
            Dispose();
        }

        public void Dispose()
        {
            Kill(null);
        }

        /// <summary>
        /// when the thread must run very tight and cache friendly tasks that won't
        /// allow the CPU to start new threads, passing the tightTasks as true
        /// would force the thread to yield after every iteration. Relaxed to true
        /// would let the runner be less reactive on new tasks added  
        /// </summary>
        /// <param name="name"></param>
        /// <param name="tightTasks"></param>
        public MultiThreadRunner(string name, bool relaxed = false, bool tightTasks = false)
        {
            var runnerData = new RunnerData(relaxed, 0, name, tightTasks);
            
            Init(name, runnerData);
        }
        
        /// <summary>
        /// Start a Multithread runner that won't take 100% of the CPU
        /// </summary>
        /// <param name="name"></param>
        /// <param name="intervalInMs"></param>
        public MultiThreadRunner(string name, float intervalInMs)
        {
            var runnerData = new RunnerData(true, intervalInMs, name, false);
            
            Init(name, runnerData);
        }

        void Init(string name, RunnerData runnerData)
        {
            _name       = name;
            _runnerData = runnerData;
#if !NETFX_CORE
            //threadpool doesn't work well with Unity apparently
            //it seems to choke when too meany threads are started
            new Thread(() => runnerData.RunCoroutineFiber()) {IsBackground = true}.Start();
#else
            Task.Factory.StartNew(() => runnerData.RunCoroutineFiber(), TaskCreationOptions.LongRunning);
#endif
        }

        public void StartCoroutine(PausableTask<T> task)
        {
            paused = false;

            _runnerData._newTaskRoutines.Enqueue(task);
            _runnerData.UnlockThread();
        }

        public void StopAllCoroutines()
        {
            _runnerData._newTaskRoutines.Clear();
            _runnerData._waitForFlush = true;

            ThreadUtility.MemoryBarrier();
        }

        public void Kill(Action onThreadKilled)
        {
            _runnerData.Kill(onThreadKilled);
        }

        class RunnerData
        {
            public RunnerData(bool relaxed, float interval, string name, bool isRunningTightTasks)
            {
                _mevent          = new ManualResetEventEx();
                _watch           = new Stopwatch();
                _coroutines      = new FasterList<PausableTask<T>>();
                _newTaskRoutines = new ThreadSafeQueue<PausableTask<T>>();
                _interval        = (long) (interval * 10000);
                _name             = name;
                _isRunningTightTasks = isRunningTightTasks;
                
                if (relaxed)
                    LockingMechanism = RelaxedLockingMechanism;
                else
                    LockingMechanism = QuickLockingMechanism;
            }

            public int Count
            {
                get
                {
                    ThreadUtility.MemoryBarrier();
                    
                    return _coroutines.Count;
                }
            }
            void QuickLockingMechanism()
            {
                var quickIterations = 0;

                while (Volatile.Read(ref _interlock) != 1 && quickIterations < 5000)
                {
                    //yielding here was slower on the 1 M points simulation
                    if (quickIterations++ > 1000)
                        ThreadUtility.Wait(quickIterations);

                    if (Volatile.Read(ref _breakThread) == true)
                        return;

                    //this is quite arbitrary at the moment as 
                    //DateTime allocates a lot in UWP .Net Native
                    //and stopwatch casues several issues
                }
                
                if (_interlock == 0 && Volatile.Read(ref _breakThread) == false)
                    RelaxedLockingMechanism();
                else
                    _interlock = 0;
            }

            void RelaxedLockingMechanism()
            {
                _mevent.Wait();

                _mevent.Reset();
            }

            void WaitForInterval()
            {
                var quickIterations = 0;
                _watch.Start();

                while (_watch.ElapsedTicks < _interval)
                {
                    ThreadUtility.Wait(quickIterations++);

                    if (Volatile.Read(ref _breakThread) == true) return;
                }

                _watch.Reset();
            }

            internal void UnlockThread()
            {
                    _interlock = 1;

                    _mevent.Set();

                    ThreadUtility.MemoryBarrier();
            }

            public void Kill(Action onThreadKilled)
            {
                if (_mevent != null) //already disposed
                {
                    _onThreadKilled = onThreadKilled;
                    _breakThread    = true;
                    ThreadUtility.MemoryBarrier();

                    UnlockThread();
                }

                if (_watch != null)
                {
                    _watch.Stop();
                    _watch = null;
                }
            }

            internal void RunCoroutineFiber()
            {
                using (var platformProfiler = new Svelto.Common.PlatformProfilerMT(_name))
                {
                    while (_breakThread == false)
                    {
                        ThreadUtility.MemoryBarrier();
                        if (_newTaskRoutines.Count > 0 && false == _waitForFlush) //don't start anything while flushing
                            _newTaskRoutines.DequeueAllInto(_coroutines);

                        for (var i = 0; i < _coroutines.Count && false == Volatile.Read(ref _breakThread); i++)
                            using (platformProfiler.Sample(_coroutines[i].ToString()))
                                StandardCoroutineProcess.StandardCoroutineIteration(ref i, _coroutines);

                        if (Volatile.Read(ref _breakThread) == false)
                        {
                            if (_interval > 0 && _waitForFlush == false) 
                                WaitForInterval();

                            if (_coroutines.Count == 0)
                            {
                                _waitForFlush = false;

                                if (_newTaskRoutines.Count == 0)
                                {
                                   LockingMechanism();
                                }

                                ThreadUtility.MemoryBarrier();
                            }
                            else
                            {
                                if (_isRunningTightTasks)
                                    ThreadUtility.Yield();
                            }
                        }
                    }

                    if (_onThreadKilled != null)
                        _onThreadKilled();

                    if (_mevent != null)
                    {
                        _mevent.Dispose();
                        _mevent = null;
                        
                        ThreadUtility.MemoryBarrier();
                    }
                }
            }
            
            public readonly ThreadSafeQueue<PausableTask<T>> _newTaskRoutines;
            public volatile bool                           _waitForFlush;

            bool _breakThread;
            
            readonly FasterList<PausableTask<T>> _coroutines;
            readonly long                      _interval;
            readonly string                    _name;
            readonly bool                      _isRunningTightTasks;
            
            ManualResetEventEx _mevent;
            Action             _onThreadKilled;
            Stopwatch          _watch;
            int                _interlock;
            System.Action      LockingMechanism;
        }

        string     _name;
        RunnerData _runnerData;
    }
}
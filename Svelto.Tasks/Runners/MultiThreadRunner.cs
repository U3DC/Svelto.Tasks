using System;
using System.Diagnostics;
using Svelto.DataStructures;
using Console = Utility.Console;
using System.Threading;
using Svelto.Utilities;

#if NETFX_CORE
using System.Threading.Tasks;
#endif

namespace Svelto.Tasks
{
    //The multithread runner always uses just one thread to run all the couroutines
    //If you want to use a separate thread, you will need to create another MultiThreadRunner
    public sealed class MultiThreadRunner : IRunner
    {
        public bool paused
        {
            set
            {
                _paused = value;
            }
            get
            {
                return _paused;
            }
        }

        public bool isStopping
        {
            get
            {
                ThreadUtility.MemoryBarrier();
                return _waitForflush == true;
            }
        }

        public int numberOfRunningTasks
        {
            get { return _coroutines.Count; }
        }

        public override string ToString()
        {
            return _name;
        }

        public void Dispose()
        {
            Kill();
        }

        public MultiThreadRunner(string name, bool relaxed = true)
        {
#if !NETFX_CORE
            var thread = new Thread(() =>
            {
                _name = name;

                RunCoroutineFiber();
            });

            thread.IsBackground = true;
#else
            var thread = new Task(() =>
            {
                _name = name;

                RunCoroutineFiber();
            });
#endif
            if (relaxed)
            {
                _lockingMechanism = RelaxedLockingMechanism;
            }
            else
            {
                _lockingMechanism = QuickLockingMechanism;
                
                _quickLockWatch = new Stopwatch();
            }
            
            _mevent = new ManualResetEventEx();

            thread.Start();
        }

        public MultiThreadRunner(string name, int intervalInMS) : this(name, false)
        {
            _interval = intervalInMS;
            _watch = new Stopwatch();
        }

        public void StartCoroutineThreadSafe(IPausableTask task)
        {
            StartCoroutine(task);
        }

        public void StartCoroutine(IPausableTask task)
        {
            paused = false;
            
            _newTaskRoutines.Enqueue(task);

            ThreadUtility.MemoryBarrier();
            if (_isAlive == false)
            {
                _isAlive = true;

                UnlockThread();
            }
        }

        public void StopAllCoroutines()
        {
            _newTaskRoutines.Clear();
            
            _waitForflush = true;
            
            ThreadUtility.MemoryBarrier();
        }

        public void Kill()
        {
            _breakThread = true;
            
            UnlockThread();
        }

        void RunCoroutineFiber()
        {
            while (_breakThread == false)
            {
                ThreadUtility.MemoryBarrier();

				if (_newTaskRoutines.Count > 0 && false == _waitForflush) //don't start anything while flushing
                    _coroutines.AddRange(_newTaskRoutines.DequeueAll());

                for (var i = 0; i < _coroutines.Count; i++)
                {
                    var enumerator = _coroutines[i];

                    try
                    { 
#if TASKS_PROFILER_ENABLED
                        bool result = Profiler.TaskProfiler.MonitorUpdateDuration(enumerator, _name);
#else
                        bool result = enumerator.MoveNext();
#endif
                        if (result == false)
                        {
                            var disposable = enumerator as IDisposable;
                            if (disposable != null)
                                disposable.Dispose();

                            _coroutines.UnorderedRemoveAt(i--);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e.InnerException != null)
                            Console.LogException(e.InnerException);
                        else
                            Console.LogException(e);

                        _coroutines.UnorderedRemoveAt(i--);
                    }
                }
                
                if (_interval > 0 && _waitForflush == false)
                {
                    _waitForInterval();
                }

                if (_coroutines.Count == 0)
                {
                    _waitForflush = false;
                    
                    if (_newTaskRoutines.Count == 0)
                    {
                        _isAlive = false;
                        
                        _lockingMechanism();
                    }
                    
                    ThreadUtility.MemoryBarrier();
                }
            }

            if (_mevent != null)
#if !(NETFX_CORE || NET_4_6)
                _mevent.Close();
#else
                _mevent.Dispose();
#endif
        }

        void _waitForInterval()
        {
            _watch.Start();
            while (_watch.ElapsedMilliseconds < _interval)
                ThreadUtility.SleepZero();
            _watch.Reset();
        }

        void QuickLockingMechanism()
        {
            _quickLockWatch.Start();

            while (Interlocked.CompareExchange(ref _interlock, 1, 1) != 1)
            {
                ThreadUtility.Yield();
                if (_quickLockWatch.ElapsedMilliseconds > 1)
                {
                    _quickLockWatch.Reset();
                    RelaxedLockingMechanism();
                }
            }
            
            _quickLockWatch.Reset();
            
            _interlock = 0;
        }

        void RelaxedLockingMechanism()
        {
            _mevent.Wait();
            
            _mevent.Reset();
        }

        void UnlockThread()
        {
            _interlock = 1;
            _mevent.Set();
            
            ThreadUtility.MemoryBarrier();
        }

        readonly FasterList<IPausableTask>      _coroutines = new FasterList<IPausableTask>();
        readonly ThreadSafeQueue<IPausableTask> _newTaskRoutines = new ThreadSafeQueue<IPausableTask>();

        string _name;
        int    _interlock;

        volatile bool _paused;
        volatile bool _isAlive;
        volatile bool _waitForflush;
        volatile bool _breakThread;

        readonly ManualResetEventEx _mevent;
        
        readonly Action    _lockingMechanism;
        readonly int       _interval;
        readonly Stopwatch _watch;
        readonly Stopwatch _quickLockWatch;
    }
}

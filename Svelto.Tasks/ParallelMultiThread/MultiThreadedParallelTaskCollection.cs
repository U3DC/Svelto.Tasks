using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Svelto.Utilities;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    /// <summary>
    /// a ParallelTaskCollection ran by MultiThreadRunner will run the tasks in a single thread
    /// MultiThreadParallelTaskCollection enables parallel tasks to run on different threads
    /// </summary>
    ///
    public class MultiThreadedParallelJobCollection<TJob> : MultiThreadedParallelTaskCollection<ParallelRunEnumerator<TJob>> where TJob: struct, IMultiThreadParallelizable
    {
        public void Add(ref TJob job, int iterations)
        {
            if (isRunning == true)
                throw new MultiThreadedParallelTaskCollectionException("can't add tasks on a started MultiThreadedParallelTaskCollection");

            var runnersLength      = _runners.Length;
            int particlesPerThread = (int) Math.Floor((double)iterations / runnersLength);
            int reminder           = iterations % runnersLength;

            for (int i = 0; i < runnersLength; i++)
                base.Add(new ParallelRunEnumerator<TJob>(ref job, particlesPerThread * i, particlesPerThread));
            
            if (reminder > 0)
                base.Add(new ParallelRunEnumerator<TJob>(ref job, particlesPerThread * runnersLength, reminder));
        }
    }

    public class MultiThreadedParallelTaskCollection: MultiThreadedParallelTaskCollection<IEnumerator>
    {
        public MultiThreadedParallelTaskCollection(uint numberOfThreads):base(numberOfThreads)
        {}

        public MultiThreadedParallelTaskCollection()
        {        
        }
    }

    public class MultiThreadedParallelTaskCollection<T> : ITaskCollection<T>, IDisposable where T:IEnumerator
    {
        public event Action onComplete;
        public event Func<Exception, bool> onException;

        public void Reset()
        {
            for (int i = 0; i < _parallelTasks.Length; i++)
                _parallelTasks[i].Reset();
        }

        T IEnumerator<T>.Current
        {
            get { throw new Exception(); }
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public TaskCollection<T>.CollectionTask Current
        {
            get { return null; }
        }

        object IEnumerator.Current { get { return null; } }

        public bool isRunning { private set; get; }

        public MultiThreadedParallelTaskCollection()
        {
            InitializeThreadsAndData((uint) Environment.ProcessorCount, false);
        }
        /// <summary>
        ///  
        /// </summary>
        /// <param name="numberOfThreads"></param>
        /// <param name="tightTasks">
        /// if several cache friendly and optimized tasks run in parallel, using tightTasks may improve parallelism
        /// as gives the chance to other threads to run.
        /// </param>
        public MultiThreadedParallelTaskCollection(uint numberOfThreads, bool tightTasks)
        {
            InitializeThreadsAndData(numberOfThreads, tightTasks);
        }

        void InitializeThreadsAndData(uint numberOfThreads, bool tightTasks)
        {
            _runners       = new MultiThreadRunner<ParallelTaskCollection<T>>[numberOfThreads];
            _taskRoutines  = new ITaskRoutine<ParallelTaskCollection<T>>[numberOfThreads];
            _parallelTasks = new ParallelTaskCollection<T>[numberOfThreads];

            //prepare a single multithread runner for each group of fiber like task collections
            //number of threads can be less than the number of tasks to run
            for (int i = 0; i < numberOfThreads; i++)
                _runners[i] = new MultiThreadRunner<ParallelTaskCollection<T>>("MultiThreadedParallelTask #".FastConcat(i), false, tightTasks);

            Action ptcOnOnComplete = DecrementConcurrentOperationsCounter;
            Func<Exception, bool> ptcOnOnException = (e) =>
                                                     {
                                                         DecrementConcurrentOperationsCounter();
                                                         return false;
                                                     };


            //prepare the fiber-like paralleltasks
            for (int i = 0; i < numberOfThreads; i++)
            {
                var ptask = TaskRunner.Instance.AllocateNewTaskRoutine<ParallelTaskCollection<T>>(_runners[i]);
                var ptc   = new ParallelTaskCollection<T>("ParallelTaskCollection #".FastConcat(i));

                ptc.onComplete  += ptcOnOnComplete;
                ptc.onException += ptcOnOnException;

                ptask.SetEnumerator(ptc);

                _parallelTasks[i] = ptc;
                _taskRoutines[i]  = ptask;
            }
        }

        bool RunMultiThreadParallelTasks()
        {
            if (_taskRoutines == null)
                throw new MultiThreadedParallelTaskCollectionException("can't run a MultiThreadedParallelTaskCollection once killed");
            
            if (isRunning == false)
            {
                _counter = _numberOfConcurrentOperationsToRun;
                ThreadUtility.MemoryBarrier();
                
                Action ptcOnOnComplete = DecrementStoppingThread;
                //start them
                for (int i = 0; i < _numberOfConcurrentOperationsToRun; i++)
                    _taskRoutines[i].Start(onStop: ptcOnOnComplete);
            }
            
            //wait for completition, I am not using signaling as this Collection could be yielded by a main thread runner
            ThreadUtility.MemoryBarrier();
            isRunning = _counter > 0; 

            return isRunning;
        }
       
        /// <summary>
        /// Add can be called by another thread, so if the collection is already running
        /// I can't allow adding more tasks.
        /// </summary>
        /// <param name="enumerator"></param>
        /// <exception cref="MultiThreadedParallelTaskCollectionException"></exception>
        public ITaskCollection<T> Add(T enumerator)
        {
            if (isRunning == true)
                throw new MultiThreadedParallelTaskCollectionException("can't add tasks on a started MultiThreadedParallelTaskCollection");

            ParallelTaskCollection<T> parallelTaskCollection = _parallelTasks[_numberOfTasksAdded++ % _parallelTasks.Length];
            parallelTaskCollection.Add(enumerator);

            _numberOfConcurrentOperationsToRun = Math.Min(_parallelTasks.Length, _numberOfTasksAdded);

            return this;
        }
        
        public bool MoveNext()
        {
            if (_isDisposing == true) return false;
            
            if (RunMultiThreadParallelTasks()) return true;
            
            if (_isDisposing == false && onComplete != null)
                onComplete();

            isRunning = false;

            return false;
        }

        public void Stop()
        {
            _stoppingThreads = _taskRoutines.Length;
            
            for (int i = 0; i < _runners.Length; i++)
                _runners[i].StopAllCoroutines();

            while (_stoppingThreads > 0) 
                ThreadUtility.TakeItEasy();
            
            isRunning = false;
            
            ThreadUtility.MemoryBarrier();
        }
        
        public void Dispose()
        {
            _isDisposing = true;
            ThreadUtility.MemoryBarrier();
            _disposingThreads = _runners.Length;

            for (int i = 0; i < _runners.Length; i++)
                _runners[i].Kill(DecrementRunningThread);
            
            while (_disposingThreads > 0) 
                ThreadUtility.TakeItEasy();

            _runners            = null;
            _parallelTasks      = null;
            onComplete          = null;
            _numberOfTasksAdded = 0;
            isRunning = false;
            
            ThreadUtility.MemoryBarrier();
        }

        void DecrementRunningThread()
        {
            Interlocked.Decrement(ref _disposingThreads);
        }
        
        void DecrementStoppingThread()
        {
            Interlocked.Decrement(ref _stoppingThreads);
        }

        void DecrementConcurrentOperationsCounter()
        {
            Interlocked.Decrement(ref _counter);
        }

        protected MultiThreadRunner<ParallelTaskCollection<T>>[]      _runners;
        ParallelTaskCollection<T>[] _parallelTasks;
        ITaskRoutine<ParallelTaskCollection<T>>[]           _taskRoutines;
        
        int                           _numberOfTasksAdded;
        int                           _numberOfConcurrentOperationsToRun;
        int                           _counter;
        int                           _disposingThreads;
        int                           _stoppingThreads;
        volatile bool                 _isDisposing;
    }

    public class MultiThreadedParallelTaskCollectionException : Exception
    {
        public MultiThreadedParallelTaskCollectionException(string canTAddEnumeratorsOnAStartedMultithreadedparalleltaskcollection):base(canTAddEnumeratorsOnAStartedMultithreadedparalleltaskcollection)
        {}
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using Svelto.DataStructures;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    public interface ITaskCollection<T> : IEnumerator<TaskCollection<T>.CollectionTask>, IEnumerator<T>
        where T : IEnumerator
    {
        event Action                onComplete;
        event Func<Exception, bool> onException;
        
        TaskCollection<T> Add(IAsyncTask asyncTask);
        TaskCollection<T> Add(T          enumerator);
        void              Clear();
        
        bool isRunning { get; }
    }

    public abstract class TaskCollection<T>:ITaskCollection<T> where T:IEnumerator
    {
        public event Action                onComplete;
        public event Func<Exception, bool> onException;
        
        public bool  isRunning { private set; get; }

        protected TaskCollection(int initialSize, string name = null):this(initialSize)
        {
            Current = new CollectionTask(this);

            _name = name ?? GetType().Name.FastConcat(GetHashCode());
        }
        
        TaskCollection(int initialSize)
        {
            _listOfStacks = FasterList<Stack<StackWrapper>>.PreFill<Stack<StackWrapper>>(initialSize);
        }
        
        public override String ToString()
        {
            return _name;
        }

        public void Dispose()
        {}

        bool IEnumerator.MoveNext()
        {
            return MoveNext();
        }

        public bool MoveNext()
        {
            isRunning = true;

            try
            {
                if (RunTasksAndCheckIfDone(ref _index) == false)
                    return true;
                
                if (onComplete != null)
                    onComplete();
            }
            catch (Exception e)
            {
                if (onException != null)
                {
                    var mustComplete = onException(e);

                    if (mustComplete)
                        isRunning = false;
                }

                throw;
            }
            
            isRunning = false;

            return false;
        }

        public TaskCollection<T> Add(IAsyncTask asyncTask)
        {
            return Add((T)new AsyncTaskWrapper(asyncTask));
        }

        public TaskCollection<T> Add(T enumerator)
        {
            Stack<StackWrapper> stack;
            if (_listOfStacks.Reuse(_listOfStacks.Count, out stack) == false)
                stack = new Stack<StackWrapper>(_INITIAL_STACK_SIZE);
            else
                stack.Clear();

            stack.Push(new StackWrapper(enumerator));
            _listOfStacks.Add(stack);

            return this;
        }
        
        object IEnumerator.Current => Current;

        public CollectionTask Current { get; }

        /// <summary>
        /// Restore the list of stacks to their original state
        /// </summary>
        void IEnumerator.Reset()
        {
            Reset();
        }

        T IEnumerator<T>.Current { get; }

        public void Reset()
        {
            var count = _listOfStacks.Count;
            for (int index = 0; index < count; ++index)
            {
                var stack = _listOfStacks[index];
                while (stack.Count > 1) stack.Pop();
                stack.Peek().enumerator.Reset();
            }

            _index = 0;
        }

        protected abstract bool RunTasksAndCheckIfDone(ref int offset);

        protected enum TaskState
        {
            doneIt,
            breakIt,
            continueIt,
            yieldIt,
        }

        protected TaskState ProcessStackAndCheckIfDone(Stack<StackWrapper> stack)
        {
            var ce = stack.Peek(); //get the current task to execute
            
            bool isDone      = !ce.enumerator.MoveNext();
            Current.current = ce.enumerator.Current;
            
            if (isDone == true)
                return TaskState.doneIt;

            {
                object returnObject = Current.current;
                if (returnObject == Break.It || returnObject == Break.AndStop)
                {
                    Current.breakIt = (Break) returnObject;

                    return TaskState.breakIt;
                }
                
                if (returnObject == null) //if null yield until next iteration
                    return TaskState.yieldIt;
                
                if (returnObject is IAsyncTask)
                    returnObject = (T)new AsyncTaskWrapper(returnObject as IAsyncTask);

                if (returnObject is ITaskRoutine<T>)
                    returnObject = (returnObject as ITaskRoutine<T>).Start();
                
                if (returnObject is T)
                    stack.Push(new StackWrapper((T)returnObject)); //push the new yielded task and execute it immediately
            }
            //if I continue down this route a TValue must be returned for last therefore it must be done

            return TaskState.continueIt;
                
        }

        public void Clear()
        {
            for (int i = 0; i < _listOfStacks.Count; i++)
                _listOfStacks[i].Clear();

            _listOfStacks.FastClear();
         
            _index = 0;
        }

        protected readonly FasterList<Stack<StackWrapper>> _listOfStacks;

        const int _INITIAL_STACK_SIZE = 1;

        public class CollectionTask
        {
            public object current { get; internal set; }

            public CollectionTask(TaskCollection<T> parent)
            {
                _parent = parent;
            }

            public void Add(T task)
            {
                _parent.Add(task);
            }

            readonly TaskCollection<T> _parent;
            public Break breakIt { internal set; get; }
        }

        readonly string         _name;

        protected struct StackWrapper
        {
            internal T                enumerator;

            public StackWrapper(T val):this()
            {
                enumerator = val;
            }
        }

        int _index;
    }
}


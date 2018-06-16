using System;
using System.Collections;
using System.Collections.Generic;
using Svelto.DataStructures;
using Svelto.Tasks.Internal;

namespace Svelto.Tasks
{
    public interface ITaskCollection<T>:IEnumerator<TaskCollection<T>.CollectionTask> where T:IEnumerator
    {}
    
    public abstract class TaskCollection:TaskCollection<IEnumerator>
    {
        protected TaskCollection(int initialSize, string name = null) : base(initialSize, name)
        {}
    }
    
    public abstract class TaskCollection<T>:ITaskCollection<T> where T:IEnumerator
    {
        public event Action                onComplete;
        public event Func<Exception, bool> onException;
        
        public bool  isRunning { protected set; get; }

        public TaskCollection(int initialSize, string name = null):this(initialSize)
        {
            _currentWrapper = new CollectionTask(this);
            
            if (name == null)
                _name = GetType().Name.FastConcat(GetHashCode());
            else
                _name = name;
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

        protected IEnumerator CreateTaskWrapper(IAsyncTask task)
        {
            return new AsyncTaskWrapper(task);
        }
        
        public TaskCollection<T> Add(IAsyncTask asyncTask)
        {
            return Add((T) CreateTaskWrapper(asyncTask));
        }

        public TaskCollection<T> Add(T enumerator)
        {
            {
                Stack<StackWrapper> stack;
                if (_listOfStacks.Reuse(_listOfStacks.Count, out stack) == false)
                    stack = new Stack<StackWrapper>(_INITIAL_STACK_SIZE);
                else
                    stack.Clear();

                stack.Push(new StackWrapper(enumerator));
                _listOfStacks.Add(stack);
            }

            return this;
        }
        
        object IEnumerator.Current
        {
            get { return _currentWrapper; }
        }
        
        public CollectionTask Current
        {
            get { return _currentWrapper; }
        }

        /// <summary>
        /// Restore the list of stacks to their original state
        /// </summary>
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
            _currentWrapper.current = ce.enumerator.Current;
            
            if (isDone == true)
                return TaskState.doneIt;

            {
                object returnObject = _currentWrapper.current;
                if (returnObject == Break.It || returnObject == Break.AndStop)
                {
                    _currentWrapper.breakIt = (Break) returnObject;

                    return TaskState.breakIt;
                }
                
                if (returnObject == null) //if null yield until next iteration
                    return TaskState.yieldIt;
                
                if (returnObject is IAsyncTask)
                    returnObject = CreateTaskWrapper(returnObject as IAsyncTask);

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

        CollectionTask _currentWrapper;
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


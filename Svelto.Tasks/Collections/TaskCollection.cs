using System;
using System.Collections;
using System.Collections.Generic;
using Svelto.DataStructures;

namespace Svelto.Tasks
{
    public interface ITaskCollection<T> : IEnumerator<TaskCollection<T>.CollectionTask>, IEnumerator<T>
        where T : IEnumerator
    {
        event Action                onComplete;
        event Func<Exception, bool> onException;
        
        ITaskCollection<T> Add(T enumerator);
        void               Clear();
        
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
            _listOfStacks = FasterList<StructFriendlyStack<T>>.PreFill<StructFriendlyStack<T>>(initialSize);
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

        public ITaskCollection<T> Add(T enumerator)
        {
            StructFriendlyStack<T> stack;
            if (_listOfStacks.Reuse(_listOfStacks.Count, out stack) == false)
                stack = new StructFriendlyStack<T>(_INITIAL_STACK_SIZE);
            else
                stack.Clear();

            stack.Push(enumerator);
            _listOfStacks.Add(stack);

            return this;
        }
        
        object IEnumerator.Current
        {
            get { return Current.current; }
        }

        public CollectionTask Current { get; }

        /// <summary>
        /// Restore the list of stacks to their original state
        /// </summary>
        void IEnumerator.Reset()
        {
            Reset();
        }

        T IEnumerator<T>.Current
        {
            get { return _currentTaskEnumerator; }
        }

        public void Reset()
        {
            var count = _listOfStacks.Count;
            for (int index = 0; index < count; ++index)
            {
                var stack = _listOfStacks[index];
                while (stack.Count > 1) stack.Pop();
                int stackIndex;
                stack.Peek(out stackIndex)[stackIndex].Reset();
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

        protected TaskState ProcessStackAndCheckIfDone(StructFriendlyStack<T> stack)
        {
            int stackIndex;
            var stacks = stack.Peek(out stackIndex);
                
            bool isDone  = !stacks[stackIndex].MoveNext();
            //_current is the tasks IEnumerator
            _currentTaskEnumerator     = stacks[stackIndex];
            //Svelto.Tasks Tasks IEnumerator are always IEnumerator returning an object
            //so Current is always an object
            Current.current = stacks[stackIndex].Current;
            
            if (isDone == true)
                return TaskState.doneIt;

            //being the IEnumerator handling always objects, it can be different things
            var returnObject = Current.current;
            //can be a Svelto.Tasks Break
            if (returnObject == Break.It || returnObject == Break.AndStop)
            {
                Current.breakIt = returnObject as Break;

                return TaskState.breakIt;
            }
            //can be a frame yield
            if (returnObject == null)
                return TaskState.yieldIt;
#if DEBUG && !PROFILER                
            if (returnObject is IAsyncTask)
                throw new ArgumentException("Svelto.Task 2.0 is not supporting IAsyncTask implicitly anymore, use AsyncTaskWrapper instead " + ToString()); 

            if (returnObject is TaskRoutine<T>)
                throw new ArgumentException("Returned a TaskRoutine without calling Start first " + ToString());
#endif            
            //can be a compatible IEnumerator  
            if (returnObject is T)
                stack.Push((T)returnObject); //push the new yielded task and execute it immediately
            
            return TaskState.continueIt;
                
        }

        public void Clear()
        {
            for (int i = 0; i < _listOfStacks.Count; i++)
                _listOfStacks[i].Clear();

            _listOfStacks.FastClear();
         
            _index = 0;
        }

        protected readonly FasterList<StructFriendlyStack<T>> _listOfStacks;

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
/*
        protected struct StackWrapper
        {
            internal T                enumerator;

            public StackWrapper(T val):this()
            {
                enumerator = val;
            }
        }*/

        int _index;
        T _currentTaskEnumerator;

        protected class StructFriendlyStack<T>
        {
            T[] _stack;
            int _nextFreeStackIndex;

            public int Count { get { return _nextFreeStackIndex; } }

            public StructFriendlyStack(int stackSize)
            {
                _stack              = new T[stackSize];
                _nextFreeStackIndex = 0;
            }

            public StructFriendlyStack()
            {      
                _stack              = new T[1];
                _nextFreeStackIndex = 0;
            }

            public void Push(T value)
            {
                // Don't reallocate before we actually want to push to it
                if (_nextFreeStackIndex == _stack.Length)
                {
                    // Double for small stacks, and increase by 20% for larger stacks
                    Array.Resize(ref _stack, _stack.Length < 100 
                                                 ? 2 *_stack.Length 
                                                 : (int) (_stack.Length * 1.2));
                }

                // Store the value, and increase reference afterwards
                _stack[_nextFreeStackIndex++] = value;
            }

            public T Pop()
            {
                if(_nextFreeStackIndex == 0)
                    throw new InvalidOperationException("The stack is empty");

                // Decrease the reference before fetching the value as
                // the reference points to the next free place
                T returnValue = _stack[--_nextFreeStackIndex]; 

                // As a safety/security measure, reset value to a default value
                _stack[_nextFreeStackIndex] = default(T);

                return returnValue;
            }

            public T[] Peek(out int index)
            {
                DBC.Tasks.Check.Require(_nextFreeStackIndex != 0);
                index = _nextFreeStackIndex - 1;
                return _stack;
            }

            public void Clear()
            {
                _nextFreeStackIndex = 0;
            }
        }
    }
}




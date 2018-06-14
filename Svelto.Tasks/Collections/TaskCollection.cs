using System;
using System.Collections;
using System.Collections.Generic;
using Svelto.DataStructures;
using Svelto.Tasks.Internal;
using Utility;

namespace Svelto.Tasks
{
    public interface ITaskCollection<T, Token>:IEnumerator where T:IEnumerator
    {}
    
    public abstract class TaskCollection<T, Token>: ITaskCollection<T, Token> where T:IEnumerator
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

        protected IEnumerator CreateTaskWrapper(IAbstractAsyncTask task)
        {
            return new AsyncTaskWrapper<Token>(task);
        }
        
        void SetToken(ActionRef<Token, IAbstractAsyncTask> action, IAbstractAsyncTask asyncTask)
        {
            action(ref _token, ref asyncTask);
        }
        
        public TaskCollection<T, Token> Add(IAbstractAsyncTask asyncTask)
        {
            var asyncTaskWrapper = new AsyncTaskWrapper<Token>(asyncTask, SetToken);
            
            return Add((T)(IEnumerator) asyncTaskWrapper);
        }

        public TaskCollection<T, Token> Add(T enumerator)
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
        
        public object Current
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
            var  returnValue = ce.enumerator.Current;
            
            if (returnValue == Break.It || returnValue == Break.AndStop)
            {
                //fallo funzionare con parallelo
                _currentWrapper.breakIt = (Break) returnValue;

                return TaskState.breakIt;
            }

            if (isDone == true)
                return TaskState.doneIt;
            
            if (returnValue == null) //if null yield until next iteration
                return TaskState.yieldIt;

            if (returnValue is IAsyncTask)
                returnValue = CreateTaskWrapper(returnValue as IAsyncTask);

            if (returnValue is ITaskRoutine<T>)
                returnValue = (returnValue as ITaskRoutine<T>).Start();
            
            if (returnValue is T)
                stack.Push(new StackWrapper((T)returnValue)); //push the new yielded task and execute it immediately

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

        internal class CollectionTask
        {
            public object current {  get {  return _parent.Current; } }

            public CollectionTask(TaskCollection<T, Token> parent)
            {
                _parent = parent;
            }

            public void Add(T task)
            {
                _parent.Add(task);
            }

            readonly TaskCollection<T, Token> _parent;
            public Break breakIt { internal set; get; }
        }

        readonly CollectionTask _currentWrapper;
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
        Token _token;
    }
}


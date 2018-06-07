using System.Collections;

namespace Svelto.Tasks
{
    public class SerialTaskCollection : SerialTaskCollection<IEnumerator>
    {
        public SerialTaskCollection()
        {}
        
        public SerialTaskCollection(string name):base(name)
        {}

        public SerialTaskCollection(int initialSize, string name = null) : base(initialSize, name)
        {}
    }

    public class SerialTaskCollection<TEnumerator> : SerialTaskCollection<TEnumerator, object>
        where TEnumerator : IEnumerator
    {
        public SerialTaskCollection()
        {}
        
        public SerialTaskCollection(string name):base(name)
        {}

        public SerialTaskCollection(int initialSize, string name = null) : base(initialSize, name)
        {}
    }
    
    public class SerialTaskCollection<T, Token>: TaskCollection<T, Token> where T:IEnumerator
    {
        const int _INITIAL_STACK_COUNT = 3;
        
        public SerialTaskCollection():base(_INITIAL_STACK_COUNT)
        {}
        
        public SerialTaskCollection(string name):base(_INITIAL_STACK_COUNT, name)
        {}

        public SerialTaskCollection(int initialSize, string name = null) : base(initialSize, name)
        {}

        public SerialTaskCollection(ref Token initialSize):base(_INITIAL_STACK_COUNT)
        {
            throw new System.NotImplementedException();
        }

        protected override bool RunTasksAndCheckIfDone(ref int offset)
        {
            var count = _listOfStacks.Count;
            while (offset < count)
            {
                var stack = _listOfStacks[offset];

                while (stack.Count > 0)
                {
                    var processStackAndCheckIfDone = ProcessStackAndCheckIfDone(stack);
                    switch (processStackAndCheckIfDone)
                    {
                        case TaskState.doneIt:
                            if (stack.Count > 1)
                                stack.Pop(); //now it can be popped, we continue the iteration
                            else
                            {
                                //in order to be able to reuse the task collection, we will keep the stack 
                                //in its original state (the original stack is not popped). 
                                offset++; //we move to the next task
                                goto breakInnerLoop;
                            }
                            break;
                        case TaskState.breakIt:
                            return true; //iteration done
                        case TaskState.continueIt:
                            //continue while loop
                            break;
                        case TaskState.yieldIt:
                            return false; //yield
                    }
                }
                
                breakInnerLoop:;
            }
            
            return true;
        }
    }
}


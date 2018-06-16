using System.Collections;
using System.Collections.Generic;

namespace Svelto.Tasks
{
    public class ParallelTaskCollection : ParallelTaskCollection<IEnumerator>
    {
        public ParallelTaskCollection()
        {}
        
        public ParallelTaskCollection(string name):base(name)
        {}

        public ParallelTaskCollection(int initialSize, string name = null) : base(initialSize, name)
        {}
        
        public ParallelTaskCollection(IEnumerator[] ptasks) : base(ptasks)
        {}
    }
    
    public class ParallelTaskCollection<T>: TaskCollection<T> where T:IEnumerator
    {
        const int _INITIAL_STACK_COUNT = 3;
        
        public ParallelTaskCollection():base(_INITIAL_STACK_COUNT)
        {}
        
        public ParallelTaskCollection(string name):base(_INITIAL_STACK_COUNT, name)
        {}

        public ParallelTaskCollection(int initialSize, string name = null) : base(initialSize, name)
        {}

        public ParallelTaskCollection(T[] ptasks):base(_INITIAL_STACK_COUNT)
        {
            for (int i = 0; i < ptasks.Length; i++)
                Add(ptasks[i]);
        }

        protected override bool RunTasksAndCheckIfDone(ref int offset)
        {
            var count = _listOfStacks.Count;
            while (count - offset > 0)
            {
                for (int index = 0; index < count - offset; ++index)
                {
                    var stack = _listOfStacks[index];

                    if (stack.Count > 0)
                    {
                        var processStackAndCheckIfDone = ProcessStackAndCheckIfDone(stack);
                        switch (processStackAndCheckIfDone)
                            {
                                case TaskState.doneIt:
                                    if (stack.Count > 1)
                                        stack.Pop(); //now it can be popped
                                    else
                                    {
                                        //in order to be able to reuse the task collection, we will keep the stack 
                                        //in its original state. The tasks will be shuffled, but due to the nature
                                        //of the parallel execution, it doesn't matter.
                                        index = RemoveStack(index, ref offset); 
                                    }
                                    break;
                                case TaskState.breakIt:
                                    return true;
                                case TaskState.continueIt:
                                    break;
                                case TaskState.yieldIt:
                                    break;
                            }
                    }
                }

                return false;
            }
            return true;
        }
        
        int RemoveStack(int index, ref int offset)
        {
            var lastIndex = _listOfStacks.Count - offset - 1;

            offset++;

            if (index == lastIndex)
                return index;

            var item = _listOfStacks[lastIndex];
            _listOfStacks[lastIndex] = _listOfStacks[index];
            _listOfStacks[index]     = item;

            return --index;
        }
    }
}


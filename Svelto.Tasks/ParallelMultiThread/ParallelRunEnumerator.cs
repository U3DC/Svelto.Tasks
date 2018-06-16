using System.Collections;

namespace Svelto.Tasks.Internal
{
    public struct ParallelRunEnumerator<TJob>: IEnumerator where TJob:struct, IMultiThreadParallelizable
    {
        public ParallelRunEnumerator(ref TJob job, int startIndex, int numberOfIterations):this()
        {
            _startIndex = startIndex;
            _numberOfITerations = numberOfIterations;
            _job = job;
        }

        public bool MoveNext()
        {
            _endIndex = _startIndex + _numberOfITerations;

            Loop();

            return false;
        }

        void Loop()
        {
            for (_index = _startIndex; _index < _endIndex; _index++)
                _job.Update(_index);
        }

        public void Reset()
        {}

        public object Current
        {
            get { return null; }
        }

        readonly int _startIndex;
        readonly int _numberOfITerations;
        readonly TJob _job;
        
        int _index;
        int _endIndex;
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace Indexing.Kernel
{
    public class TimerPool
    {
        private ConcurrentBag<Timer> _pool = new ConcurrentBag<Timer>();
        public const int IntervalMS = 250;

        public Timer Get()
        {
            Timer result;
            if (_pool.TryTake(out result)) return result;
            return new Timer() { AutoReset = false, Interval = IntervalMS };
        }

        public void Put(Timer timer)
        {
            _pool.Add(timer);
        }
    }
}

using System;
using System.Timers;

namespace NOVAxis.Utilities
{
    public class Timer : IDisposable
    {
        private bool _disposed;
        private System.Timers.Timer _timer;
        
        public bool IsSet { get; private set; }
        public bool Elapsed { get; private set; }

        public void Set(double interval, ElapsedEventHandler elapsedEvent)
        {
            _timer = new System.Timers.Timer(interval);
            _timer.Elapsed += (sender, e) => Elapsed = true;
            _timer.Elapsed += elapsedEvent;
            IsSet = true;
        }

        public void Reset()
        {
            Stop(); Start();
            Elapsed = false;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            if (_disposed)
                return;

            _timer.Dispose();
            IsSet = false;
            Elapsed = false;

            _disposed = true;
        }
    }
}
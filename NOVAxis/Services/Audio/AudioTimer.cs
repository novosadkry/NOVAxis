using System;
using System.Timers;

namespace NOVAxis.Services.Audio
{
    public class AudioTimer : IDisposable
    {
        private Timer _timer;
        public bool IsSet { get; private set; }
        public bool Elapsed { get; private set; }

        public void Set(double interval, ElapsedEventHandler elapsedEvent)
        {
            _timer = new Timer(interval);
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
            _timer.Dispose();
            IsSet = false;
            Elapsed = false;
        }
    }
}
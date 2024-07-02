using System.Diagnostics;

namespace EgsExporter
{
    internal class Timer : IDisposable
    {
        private readonly Action<TimeSpan>? _afterAction;
        private readonly Stopwatch _stopwatch;

        public Timer(Action<TimeSpan> afterAction)
        {
            _afterAction = afterAction;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _afterAction?.Invoke(_stopwatch.Elapsed);
        }
    }
}

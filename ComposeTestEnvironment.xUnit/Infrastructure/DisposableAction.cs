using System;
using System.Threading;

namespace ComposeTestEnvironment.xUnit.Infrastructure
{
    internal sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        private int _isDisposed;

        public DisposableAction(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            _action();
        }
    }
}

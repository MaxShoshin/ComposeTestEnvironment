using System;
using System.Threading;
using System.Threading.Tasks;

namespace ComposeTestEnvironment.xUnit.Infrastructure
{
    internal sealed class DisposableAsyncAction : IAsyncDisposable
    {
        private readonly Func<Task> _action;
        private int _isDisposed;

        public DisposableAsyncAction(Func<Task> action)
        {
            _action = action;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            await _action().ConfigureAwait(false);
        }
    }
}

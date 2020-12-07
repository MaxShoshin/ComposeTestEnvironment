using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ComposeTestEnvironment.xUnit.Infrastructure
{
    internal sealed class DisposableList : IDisposable, IAsyncDisposable
    {
        private readonly List<object> _disposables = new();
        private bool _isDisposed;

        public void Add(IDisposable? disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (_isDisposed)
            {
                disposable.Dispose();
            }
            else
            {
                _disposables.Add(disposable);
            }
        }

        public async ValueTask AddAsync(IAsyncDisposable? disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (_isDisposed)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _disposables.Add(disposable);
            }
        }

        public void Dispose()
        {
            var valueTask = DisposeAsync();
            if (!valueTask.IsCompleted)
            {
                valueTask.AsTask().GetAwaiter().GetResult();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            var disposeList = _disposables.ToList();
            _disposables.Clear();
            disposeList.Reverse();

            foreach (var disposable in disposeList)
            {
                if (disposable is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }

                (disposable as IDisposable)?.Dispose();
            }
        }
    }
}

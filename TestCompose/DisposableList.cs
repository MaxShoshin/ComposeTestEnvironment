using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCompose
{
    /// <summary>
    /// Утилитный класс позволяющий вызывать Dispose у нескольких IDisposable инстансов.
    /// </summary>
    public sealed class DisposableList : IDisposable, IAsyncDisposable
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
                await disposable.DisposeAsync();
            }
            else
            {
                _disposables.Add(disposable);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var valueTask = DisposeAsync();
            if (!valueTask.IsCompleted)
            {
                valueTask.AsTask().GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc />
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
                    await asyncDisposable.DisposeAsync();
                }

                (disposable as IDisposable)?.Dispose();
            }
        }
    }
}

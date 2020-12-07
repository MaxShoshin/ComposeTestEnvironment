using System;
using System.Threading;
using System.Threading.Tasks;

namespace ComposeTestEnvironment.xUnit.Infrastructure
{
    internal static class TaskExtensions
    {
        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            using (var cancellationSource = new CancellationTokenSource(timeout))
            {
                return await task.WithCancellation(cancellationSource.Token).ConfigureAwait(false);
            }
        }

        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);

            // This disposes the registration as soon as one of the tasks trigger
            using (cancellationToken.Register(state => { ((TaskCompletionSource<byte>)state!).TrySetResult(1); }, tcs))
            {
                var resultTask = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);

                if (resultTask == tcs.Task)
                {
                    // Operation cancelled
                    throw new OperationCanceledException(cancellationToken);
                }

                return await task.ConfigureAwait(false);
            }
        }
    }
}

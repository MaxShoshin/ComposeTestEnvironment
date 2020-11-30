using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace ComposeTestEnvironment.xUnit
{
    public static class DisposableListExtensions
    {
        public static void Add([NotNull] this DisposableList list, [NotNull] Action actionOnDispose)
        {
            list.Add(new DisposableAction(actionOnDispose));
        }

        public static async ValueTask AddAsync([NotNull] this DisposableList list, [NotNull] Func<Task> actionOnDispose)
        {
            await list.AddAsync(new DisposableAsyncAction(actionOnDispose));
        }
    }
}

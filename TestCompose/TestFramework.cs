using System;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TestCompose
{
    /// <summary>
    /// Special test framework is used to guarantee cleanup resources after test execution.
    /// </summary>
    public sealed class TestFramework : XunitTestFramework
    {
        private static DisposalTracker? disposables;

        public TestFramework(IMessageSink messageSink)
            : base(messageSink)
        {
        }

        public static void RegisterDisposable(IDisposable disposable)
        {
            disposables!.Add(disposable);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000", Justification = "Added to disposables collection")]
        protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo)
        {
            var discoverer = base.CreateDiscoverer(assemblyInfo);

            // HACK: Use disposable tracker from Discoverer as current instance DisposableTracker is
            // called inside `async void Dispose()` after Task.Delay(1) i.e. dispose code can be omitted.
            // This Dispose behavior will changed in the future versions of xUnit (v.3)
            var property = typeof(TestFrameworkDiscoverer).GetProperty("DisposalTracker", BindingFlags.Instance | BindingFlags.NonPublic)
                           ?? throw new InvalidOperationException("Cannot find property «DisposalTracker» on discoverer");
            disposables = (DisposalTracker?)property.GetValue(discoverer)
                          ?? throw new InvalidOperationException("No disposal tracker found");

            return discoverer;
        }
    }
}
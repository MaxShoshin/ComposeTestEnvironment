﻿using System;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace ComposeTestEnvironment.xUnit
{
    /// <summary>
    /// Special test framework is used to guarantee cleanup resources after test execution.
    /// </summary>
    public sealed class TestFramework : XunitTestFramework
    {
        private static DisposalTracker? _disposables;
        private static bool _initialized;

        public TestFramework(IMessageSink messageSink)
            : base(messageSink)
        {
            _initialized = true;
        }

        public static void RegisterDisposable(IDisposable disposable)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Missing registration of TestFramework. You should add:" + Environment.NewLine +
                    "[assembly: Xunit.TestFramework(\"ComposeTestEnvironment.xUnit.TestFramework\", \"ComposeTestEnvironment.xUnit\")]");
            }

            _disposables!.Add(disposable);
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
            _disposables = (DisposalTracker?)property.GetValue(discoverer)
                          ?? throw new InvalidOperationException("No disposal tracker found");

            return discoverer;
        }
    }
}

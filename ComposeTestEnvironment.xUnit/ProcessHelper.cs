using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace ComposeTestEnvironment.xUnit
{
    internal sealed class ProcessHelper : IDisposable
    {
        private readonly List<string> _arguments = new();
        private readonly ProcessStartInfo _startInfo;
        private readonly Process _process;
        private readonly TaskCompletionSource<int> _exited = new();
        private readonly List<Task<byte>> _startedTasks = new();

        public ProcessHelper([NotNull] string processName)
        {
            _startInfo = new ProcessStartInfo(processName);
            _startInfo.RedirectStandardError = true;
            _startInfo.RedirectStandardOutput = true;

            _process = new Process();
            _process.EnableRaisingEvents = true;
            _process.StartInfo = _startInfo;

            var started = new TaskCompletionSource<byte>();
            started.SetResult(1);
            _startedTasks.Add(started.Task);
        }

        public ProcessHelper Argument([NotNull] string argument)
        {
            _arguments.Add(Encode(argument));

            return this;
        }

        public ProcessHelper Argument([NotNull] string name, [NotNull] string value)
        {
            return Argument(name).Argument(value);
        }

        public ProcessHelper CollectOutput([NotNull] Action<string> processOutput)
        {
            _process.OutputDataReceived += OutputDataReceived;
            _process.ErrorDataReceived += OutputDataReceived;

            return this;

            void OutputDataReceived(object sender, DataReceivedEventArgs args)
            {
                if (args.Data == null)
                {
                    return;
                }

                processOutput(args.Data);
            }
        }

        public ProcessHelper WaitForMessageInOutput(string message, int count = 1)
        {
            var started = new TaskCompletionSource<byte>();
            _startedTasks.Add(started.Task);

            var output = new StringBuilder();

            var currentIndex = 0;
            void OnProcessOnOutputDataReceived(object sender, DataReceivedEventArgs args)
            {
                if (args.Data == null)
                {
                    return;
                }

                if (!started.Task.IsCompleted)
                {
                    output.AppendLine(args.Data);
                }

                if (args.Data.Contains(message, StringComparison.Ordinal))
                {
                    currentIndex++;

                    if (currentIndex >= count)
                    {
                        started.TrySetResult(1);
                    }
                }
            }

            _process.Exited += (_, _) =>
            {
                started.TrySetException(new InvalidOperationException("Exited before started. Output: " + output));
            };

            _process.OutputDataReceived += OnProcessOnOutputDataReceived;
            _process.ErrorDataReceived += OnProcessOnOutputDataReceived;

            return this;
        }

        public async Task Start(TimeSpan timeout)
        {
            _startInfo.Arguments = string.Join(" ", _arguments);

            _process.Exited += Exited;

            if (!_process.Start())
            {
                throw new InvalidOperationException("Unable to start process.");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await Task.WhenAll(_startedTasks).WithTimeout(timeout);
        }

        public Task<int> WaitForExit()
        {
            return _exited.Task;
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }

            _process.Dispose();
        }

        private void Exited(object? sender, EventArgs? e)
        {
            // Flush output
            _process.WaitForExit();

            _exited.SetResult(_process.ExitCode);
        }

        private string Encode(string argument)
        {
            argument = argument.Trim();

            if (argument.Contains(' ', StringComparison.InvariantCulture) || argument.Contains('"', StringComparison.InvariantCulture))
            {
                return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            }

            return argument;
        }
    }
}

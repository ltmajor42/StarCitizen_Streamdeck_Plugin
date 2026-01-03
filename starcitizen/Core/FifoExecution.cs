using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace starcitizen.Core
{
    /// <summary>
    /// FIFO work queue using modern System.Threading.Channels.
    /// Provides efficient, lock-free, async-friendly work item processing.
    /// </summary>
    public sealed class FifoExecution : IDisposable
    {
        private readonly Channel<Action> _channel;
        private readonly Task _workerTask;
        private readonly CancellationTokenSource _cts;
        private volatile bool _disposed;

        public FifoExecution()
        {
            _channel = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        /// <summary>
        /// Queues a work item for FIFO execution.
        /// </summary>
        public void QueueUserWorkItem(WaitCallback callback, object state)
        {
            if (_disposed || _cts.IsCancellationRequested) return;

            // Capture execution context for proper async flow
            var context = ExecutionContext.Capture();
            
            _channel.Writer.TryWrite(() =>
            {
                if (_disposed) return; // Skip if disposed during queue wait
                
                if (context != null)
                {
                    ExecutionContext.Run(context, _ => callback(state), null);
                }
                else
                {
                    callback(state);
                }
            });
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                await foreach (var workItem in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (_disposed) break;

                    try
                    {
                        workItem();
                    }
                    catch (Exception ex)
                    {
                        // Log but don't crash the worker
                        try { PluginLog.Error($"FifoExecution work item failed: {ex.Message}"); } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                try { PluginLog.Error($"FifoExecution processor crashed: {ex.Message}"); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Signal cancellation
                _cts.Cancel();
                
                // Complete the channel to stop accepting new items
                _channel.Writer.TryComplete();
                
                // Wait briefly for worker to finish
                if (!_workerTask.Wait(TimeSpan.FromMilliseconds(500)))
                {
                    PluginLog.Warn("FifoExecution: Worker task did not complete in time");
                }
            }
            catch (AggregateException)
            {
                // Expected if task was cancelled
            }
            catch (Exception ex)
            {
                try { PluginLog.Error($"FifoExecution.Dispose error: {ex.Message}"); } catch { }
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}

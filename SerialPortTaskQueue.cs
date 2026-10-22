// ============================================================================
//  SerialPortTaskQueue.cs  (自动重试版)
//  ------------------------------------------------------------------------
//  与上一版区别:
//    上一版:被插队 → 任务取消,调用方 await 抛 OperationCanceledException
//    本版  :被插队 → 默默重新入队,调用方 await 仍然有效,直到真正完成/超时
//
//  实现关键:双 TCS
//    • OuterTcs  - 给调用方 await,生命周期 = 整个任务
//    • InnerTcs  - 内部用,被插队时丢弃并新建,接收线程只 SetResult 它
//    • 响应到达 → InnerTcs.Result 透传到 OuterTcs
//
//  协议层 FrameParser 沿用上一版
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace SerialDemo
{
    // ==========================================================================
    //  Layer 1: 协议解析器(长度字段法 + CRC16/MODBUS)
    //  帧格式:[0xAA 0x55] [LEN:2B LE] [DATA:LEN B] [CRC16:2B LE]
    // ==========================================================================
    public static class FrameParser
    {
        public const byte H1 = 0xAA;
        public const byte H2 = 0x55;

        public static byte[] Pack(byte[] payload)
        {
            if (payload == null) payload = new byte[0];
            int len = payload.Length;
            var f = new byte[2 + 2 + len + 2];
            int p = 0;
            f[p++] = H1; f[p++] = H2;
            f[p++] = (byte)(len & 0xFF);
            f[p++] = (byte)(len >> 8);
            Buffer.BlockCopy(payload, 0, f, p, len);
            p += len;
            ushort crc = Crc16(f, 4, len);
            f[p++] = (byte)(crc & 0xFF);
            f[p++] = (byte)(crc >> 8);
            return f;
        }

        public static bool TryParse(List<byte> buf, out byte[] frame)
        {
            frame = null;
            int head = -1;
            for (int i = 0; i <= buf.Count - 2; i++)
                if (buf[i] == H1 && buf[i + 1] == H2) { head = i; break; }
            if (head < 0)
            {
                if (buf.Count > 0) buf.Clear();
                return false;
            }
            if (head > 0) buf.RemoveRange(0, head);
            if (buf.Count < 4) return false;
            int len = buf[2] | (buf[3] << 8);
            int total = 2 + 2 + len + 2;
            if (buf.Count < total) return false;

            ushort recv = (ushort)(buf[total - 2] | (buf[total - 1] << 8));
            ushort calc = Crc16(buf, 2, 2 + len);
            if (recv != calc) { buf.RemoveRange(0, 2); return false; }

            frame = buf.GetRange(4, len).ToArray();
            buf.RemoveRange(0, total);
            return true;
        }

        public static ushort Crc16(IList<byte> data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int b = 0; b < 8; b++)
                    crc = (ushort)(((crc & 1) != 0) ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
            return crc;
        }
    }

    // ==========================================================================
    //  Layer 2+3: 串口任务队列(自动重试版)
    // ==========================================================================
    public sealed class SerialPortTaskQueue : IDisposable
    {
        public enum Priority { Low = 0, Normal = 1, High = 2 }

        // ---------- 串口 ----------
        private readonly SerialPort _port;

        // ---------- 发送队列分层 ----------
        private readonly ConcurrentQueue<Waiter> _qHigh   = new ConcurrentQueue<Waiter>();
        private readonly ConcurrentQueue<Waiter> _qNormal = new ConcurrentQueue<Waiter>();
        private readonly ConcurrentQueue<Waiter> _qLow    = new ConcurrentQueue<Waiter>();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);

        // ---------- 插队信号 ----------
        private readonly SemaphoreSlim _preemptSignal = new SemaphoreSlim(0);
        private int _pendingPreemptCount;

        // ---------- 等待匹配列表 ----------
        private readonly List<Waiter> _waiters = new List<Waiter>();
        private readonly object _waiterLock = new object();
        private Waiter _currentWaiter;

        // ---------- 接收缓存 ----------
        private readonly List<byte> _rxBuf = new List<byte>(4096);
        private readonly object _rxLock = new object();

        // ---------- 后台 worker ----------
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _senderWorker;
        private volatile bool _stopped;

        public int PendingCount { get { lock (_waiterLock) return _waiters.Count; } }
        public bool IsStopped => _stopped;

        // ==========================================================================
        //  构造与销毁
        // ==========================================================================
        public SerialPortTaskQueue(SerialPort port)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _port.DataReceived += OnDataReceived;
            _senderWorker = Task.Factory.StartNew(
                SenderLoopAsync,
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (_stopped) return;
            _stopped = true;
            _port.DataReceived -= OnDataReceived;
            _sendSignal.Release();
            _preemptSignal.Release();
            try { _senderWorker.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Cancel();
            _cts.Dispose();
            _sendSignal.Dispose();
            _preemptSignal.Dispose();
        }

        // ==========================================================================
        //  公共 API
        // ==========================================================================

        /// <summary>
        /// 入队一个任务,异步等待匹配响应。
        /// ★ 被插队时会自动重试,调用方 await 不会抛 OperationCanceledException
        /// ★ 只有真正超时 / 串口错误 / Dispose 时才会抛异常
        /// </summary>
        public Task<byte[]> EnqueueAsync(
            byte[] data,
            Func<byte[], bool> matcher,
            TimeSpan timeout,
            Priority priority = Priority.Normal,
            CancellationToken ct = default)
        {
            if (data == null || data.Length == 0) return Task.FromResult<byte[]>(null);
            if (matcher == null) throw new ArgumentNullException(nameof(matcher));
            if (_stopped) throw new InvalidOperationException("队列已停止");

            // 外层 TCS:整个任务生命周期,给调用方 await
            var outerTcs = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // 内层 TCS:被插队时轮转,接收线程只 SetResult 它
            var innerTcs = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var waiter = new Waiter
            {
                Data = data,
                Matcher = matcher,
                OuterTcs = outerTcs,
                InnerTcs = innerTcs,
                Priority = priority,
            };

            // 注册到等待列表(接收线程会匹配 InnerTcs)
            lock (_waiterLock) _waiters.Add(waiter);

            // 入发送队列
            EnqueueSender(waiter);
            _sendSignal.Release();

            // 插队信号
            if (priority == Priority.High)
            {
                Interlocked.Increment(ref _pendingPreemptCount);
                _preemptSignal.Release();
            }

            // ★ 总超时(覆盖整个生命周期,包括重试时间)
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            linked.Token.Register(() =>
            {
                // 超时/取消:同时 cancel 外层和内层
                lock (_waiterLock) _waiters.Remove(waiter);
                innerTcs.TrySetCanceled();
                outerTcs.TrySetCanceled();
            });

            return outerTcs.Task;   // 调用方只看到 outer
        }

        /// <summary>只发不收</summary>
        public Task EnqueueFireAndForget(
            byte[] data,
            Priority priority = Priority.Normal)
        {
            // matcher 永远 false → OuterTcs 永远不被响应匹配完成
            // 在 Dispose 时被清理
            return EnqueueAsync(data, _ => false, TimeSpan.FromHours(1), priority);
        }

        private void EnqueueSender(Waiter w)
        {
            switch (w.Priority)
            {
                case Priority.High:   _qHigh.Enqueue(w);   break;
                case Priority.Normal: _qNormal.Enqueue(w); break;
                case Priority.Low:    _qLow.Enqueue(w);    break;
            }
        }

        // ==========================================================================
        //  发送循环(关键:InnerTcs 轮转)
        // ==========================================================================
        private async Task SenderLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try { await _sendSignal.WaitAsync(_cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                if (_stopped) break;

                if (!TryDequeueSender(out var w)) continue;

                lock (_waiterLock) _currentWaiter = w;

                try
                {
                    // 1) 发送
                    var frame = FrameParser.Pack(w.Data);
                    await Task.Run(() =>
                    {
                        _port.Write(frame, 0, frame.Length);
                        var sw = new SpinWait();
                        while (_port.BytesToWrite > 0) sw.SpinOnce();
                    }, _cts.Token).ConfigureAwait(false);

                    // 2) 等 InnerTcs 完成 OR 被插队
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                    {
                        var responseTask = w.InnerTcs.Task;
                        var preemptTask = WaitForPreemptAsync(linked.Token);

                        var winner = await Task.WhenAny(responseTask, preemptTask)
                            .ConfigureAwait(false);

                        if (winner == preemptTask)
                        {
                            // ★ 真插队! 轮转 InnerTcs,任务重新入队
                            //    注意:OuterTcs 完全不动
                            linked.Cancel();

                            // 替换为新的 InnerTcs
                            w.InnerTcs = new TaskCompletionSource<byte[]>(
                                TaskCreationOptions.RunContinuationsAsynchronously);

                            // 重新挂到等待列表(用新 InnerTcs 接收响应)
                            // 注意:不能简单 Remove+Add,因为别的代码可能持有了 waiter 引用
                            // 但这里只有 sender + 接收线程访问,加锁后安全
                            lock (_waiterLock)
                            {
                                _currentWaiter = null;
                            }

                            // 重新入队为 Normal(可改成原优先级)
                            _qNormal.Enqueue(w);
                            _sendSignal.Release();
                            continue;   // ★ 不退出 worker,继续下一轮
                        }

                        // 响应到了:把 InnerTcs 结果透传到 OuterTcs
                        if (responseTask.Status == TaskStatus.RanToCompletion)
                        {
                            w.OuterTcs.TrySetResult(responseTask.Result);
                        }
                        else if (responseTask.Status == TaskStatus.Canceled)
                        {
                            // InnerTcs 被取消(总超时触发) → OuterTcs 也取消
                            w.OuterTcs.TrySetCanceled();
                        }
                        else if (responseTask.Status == TaskStatus.Faulted)
                        {
                            w.OuterTcs.TrySetException(responseTask.Exception.InnerException);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    lock (_waiterLock) _currentWaiter = null;
                    lock (_waiterLock) _waiters.Remove(w);
                    w.OuterTcs.TrySetException(ex);
                }
            }

            // 退出:清空所有 tcs
            while (TryDequeueSender(out var left))
            {
                left.OuterTcs.TrySetCanceled();
            }
        }

        private async Task WaitForPreemptAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _pendingPreemptCount) > 0)
            {
                await _preemptSignal.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Decrement(ref _pendingPreemptCount);
                return;
            }
            await _preemptSignal.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Decrement(ref _pendingPreemptCount);
        }

        private bool TryDequeueSender(out Waiter w)
        {
            if (_qHigh.TryDequeue(out w))   return true;
            if (_qNormal.TryDequeue(out w)) return true;
            if (_qLow.TryDequeue(out w))    return true;
            return false;
        }

        // ==========================================================================
        //  接收(在 DataReceived 事件线程)
        //  ★ 只对 InnerTcs 调 SetResult,不碰 OuterTcs
        // ==========================================================================
        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int n = _port.BytesToRead;
                if (n <= 0) return;
                var tmp = new byte[n];
                int read = _port.Read(tmp, 0, n);
                if (read <= 0) return;

                List<byte[]> frames;
                lock (_rxLock)
                {
                    _rxBuf.AddRange(tmp);
                    frames = new List<byte[]>();
                    while (FrameParser.TryParse(_rxBuf, out var frame))
                        frames.Add(frame);
                    if (_rxBuf.Count > 65536) _rxBuf.Clear();
                }

                foreach (var frame in frames)
                    TryMatchFrame(frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Rx] {ex.Message}");
            }
        }

        private void TryMatchFrame(byte[] frame)
        {
            Waiter hit = null;
            lock (_waiterLock)
            {
                // 优先匹配"当前任务"
                if (_currentWaiter != null
                    && !_currentWaiter.InnerTcs.Task.IsCompleted
                    && _currentWaiter.Matcher(frame))
                {
                    hit = _currentWaiter;
                    _currentWaiter = null;
                }
                else
                {
                    // 扫描所有 waiter
                    for (int i = 0; i < _waiters.Count; i++)
                    {
                        var w = _waiters[i];
                        if (!w.InnerTcs.Task.IsCompleted && w.Matcher(frame))
                        {
                            hit = w;
                            break;
                        }
                    }
                    if (hit != null) _waiters.Remove(hit);
                }
            }
            // ★ 只 SetResult InnerTcs,OuterTcs 由 sender 线程负责
            hit?.InnerTcs.TrySetResult(frame);
        }

        // ==========================================================================
        //  内部:Waiter(双 TCS)
        // ==========================================================================
        private class Waiter
        {
            public byte[] Data;
            public Func<byte[], bool> Matcher;

            /// <summary>对外:整个任务生命周期</summary>
            public TaskCompletionSource<byte[]> OuterTcs;

            /// <summary>对内:每次重试用,接收线程只 SetResult 它</summary>
            public TaskCompletionSource<byte[]> InnerTcs;

            public Priority Priority;
        }
    }
}

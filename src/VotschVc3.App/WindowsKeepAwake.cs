using System.Runtime.InteropServices;

namespace VotschVc3.App;

/// <summary>Thread-scoped power request and zero-distance activity; never changes Windows settings.</summary>
public sealed class WindowsKeepAwake : IDisposable
{
    public const uint Continuous = 0x80000000;
    public const uint Required = Continuous | 0x1 | 0x2;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _thread;
    private int _disposed;

    public WindowsKeepAwake(Action<string> warning)
        : this(SetThreadExecutionState, SendActivity, warning, TimeSpan.FromSeconds(10)) { }

    // Injectable native boundary: tests must not change the workstation's power or input state.
    public WindowsKeepAwake(Func<uint, uint> power, Func<bool> activity, Action<string> warning, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _thread = new Thread(() =>
        {
            bool powerWarned = false, inputWarned = false;
            try
            {
                do
                {
                    if (power(Required) == 0 && !powerWarned)
                    {
                        powerWarned = true;
                        warning("Windows nepotvrdil požiadavku na zabránenie uspatiu a zhasnutiu displeja.");
                    }
                    if (!activity() && !inputWarned)
                    {
                        inputWarned = true;
                        warning("Windows neprijal obnovenie aktivity. Automatické zamknutie môže zostať aktívne.");
                    }
                } while (!_stop.Wait(interval));
            }
            catch (Exception ex) { warning("Udržiavanie PC v aktívnom stave: " + ex.Message); }
            finally { power(Continuous); }
        }) { IsBackground = true, Name = "Lab Control keep awake" };
        _thread.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set();
        _thread.Join();
        _stop.Dispose();
    }

    private static bool SendActivity()
    {
        // Relative move of (0, 0): no cursor displacement, clicks or key strokes.
        // Windows/UIPI may reject this on a locked or secure desktop; never attempt to unlock it.
        var input = new Input { Mouse = new MouseInput { Flags = 0x0001 } };
        return SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}

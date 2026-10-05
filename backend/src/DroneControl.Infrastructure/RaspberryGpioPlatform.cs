using System.Runtime.InteropServices;

namespace DroneControl.Infrastructure;

/// <summary>
/// Concrete Linux GPIO platform operating the kernel GPIO character device
/// through <b>libgpiod</b> (the standard userspace GPIO library — the same one
/// the <c>gpioset</c> tool uses; installed in the backend image). The chip path
/// comes from configuration at the composition root; the shipped default
/// (<c>/dev/gpiochip0</c>) is the target-detected value recorded in
/// `docs/hardware/raspberry-pi-inventory.md`, not an invented device path.
/// Line identifiers are the validated GPIO configuration numbers.
/// <para>
/// When the chip is unavailable or not accessible (for example the target's
/// root-only <c>gpiochip0</c> without a group grant), every operation throws
/// with the errno reason — the provider surfaces that through the existing
/// operational-failure channel (honest Unavailable/503); nothing falls back to
/// simulated behavior. All owned lines and the chip are released deterministically.
/// </para>
/// </summary>
public sealed class RaspberryGpioPlatform : IRaspberryGpioPlatform
{
    private const string Consumer = "CarDrone-GPIO";

    private readonly object _gate = new();
    private readonly string _chipPath;
    private readonly Dictionary<int, IntPtr> _lines = [];
    private IntPtr _chip = IntPtr.Zero;
    private bool _disposed;

    /// <summary>Validated chip path is required; empty is a composition defect.</summary>
    public RaspberryGpioPlatform(string chipPath)
        => _chipPath = string.IsNullOrWhiteSpace(chipPath)
            ? throw new ArgumentException("A GPIO chip path is required.", nameof(chipPath))
            : chipPath;

    /// <inheritdoc />
    public void ConfigureOutput(int lineIdentifier)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lines.ContainsKey(lineIdentifier))
            {
                return; // Idempotent: the line is already requested as an output.
            }

            EnsureChipOpen();

            var line = LibGpiod.ChipGetLine(_chip, checked((uint)lineIdentifier));
            if (line == IntPtr.Zero)
            {
                throw Failure($"Failed to get GPIO line {lineIdentifier} on '{_chipPath}'");
            }

            if (LibGpiod.LineRequestOutput(line, Consumer, 0) != 0)
            {
                throw Failure($"Failed to request GPIO line {lineIdentifier} as an output on '{_chipPath}'");
            }

            _lines.Add(lineIdentifier, line);
        }
    }

    /// <inheritdoc />
    public void Write(int lineIdentifier, GpioPinValue value)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_lines.TryGetValue(lineIdentifier, out var line))
            {
                throw new InvalidOperationException($"GPIO line {lineIdentifier} is not configured as an output.");
            }

            // Transport-level level only: Low=0, High=1 — no motor semantics.
            if (LibGpiod.LineSetValue(line, (int)value) != 0)
            {
                throw Failure($"Failed to write GPIO line {lineIdentifier} on '{_chipPath}'");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var line in _lines.Values)
            {
                LibGpiod.LineRelease(line);
            }

            _lines.Clear();
            if (_chip != IntPtr.Zero)
            {
                LibGpiod.ChipClose(_chip);
                _chip = IntPtr.Zero;
            }
        }
    }

    private void EnsureChipOpen()
    {
        if (_chip != IntPtr.Zero)
        {
            return;
        }

        var chip = LibGpiod.ChipOpen(_chipPath);
        if (chip == IntPtr.Zero)
        {
            throw Failure($"Failed to open GPIO chip '{_chipPath}'");
        }

        _chip = chip;
    }

    private static InvalidOperationException Failure(string message)
    {
        var errno = Marshal.GetLastPInvokeError();
        return new InvalidOperationException(
            $"{message}. errno={errno} ({Marshal.GetPInvokeErrorMessage(errno)})");
    }

    private static class LibGpiod
    {
        // Debian/Ubuntu ship libgpiod v1 (soname libgpiod.so.2).
        private const string Library = "libgpiod.so.2";

        [DllImport(Library, EntryPoint = "gpiod_chip_open", SetLastError = true)]
        internal static extern IntPtr ChipOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

        [DllImport(Library, EntryPoint = "gpiod_chip_close")]
        internal static extern void ChipClose(IntPtr chip);

        [DllImport(Library, EntryPoint = "gpiod_chip_get_line", SetLastError = true)]
        internal static extern IntPtr ChipGetLine(IntPtr chip, uint offset);

        [DllImport(Library, EntryPoint = "gpiod_line_request_output", SetLastError = true)]
        internal static extern int LineRequestOutput(
            IntPtr line, [MarshalAs(UnmanagedType.LPUTF8Str)] string consumer, int defaultVal);

        [DllImport(Library, EntryPoint = "gpiod_line_set_value", SetLastError = true)]
        internal static extern int LineSetValue(IntPtr line, int value);

        [DllImport(Library, EntryPoint = "gpiod_line_release")]
        internal static extern void LineRelease(IntPtr line);
    }
}
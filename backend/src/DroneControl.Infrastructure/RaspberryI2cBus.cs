using System.Runtime.InteropServices;

namespace DroneControl.Infrastructure;

/// <summary>
/// Concrete Linux I2C bus operating a <c>/dev/i2c-N</c> character device
/// through libc (<c>open</c> / <c>ioctl I2C_SLAVE</c> / <c>write</c> /
/// <c>read</c>) — no third-party package, mirroring the GPIO approach. The bus
/// path comes from configuration at the composition root; the shipped default
/// (<c>/dev/i2c-1</c>) is the target-observed node recorded in
/// <c>docs/hardware/raspberry-pi-inventory.md</c>, not an invented path.
/// <para>
/// Reads are the only transactions this task performs (the one-byte register
/// pointer is part of every I2C read). When the bus is missing or not
/// accessible, every operation throws with the errno reason; the battery
/// monitor turns that into an honest unavailable status — never a fabricated
/// reading and never a silent fallback.
/// </para>
/// </summary>
public sealed class RaspberryI2cBus : II2cBus
{
    private const int ORdwr = 0x0002;

    // ioctl request is `unsigned long` on Linux; a 32-bit declaration truncates
    // the value on arm64 and the kernel rejects the call (same class of bug
    // already fixed for the GPIO platform).
    private const ulong I2cSlave = 0x0703;

    private readonly object _gate = new();
    private readonly string _busPath;
    private int _fd = -1;
    private bool _disposed;

    /// <summary>Validated bus path is required; empty is a composition defect.</summary>
    public RaspberryI2cBus(string busPath)
        => _busPath = string.IsNullOrWhiteSpace(busPath)
            ? throw new ArgumentException("An I2C bus path is required.", nameof(busPath))
            : busPath;

    /// <inheritdoc />
    public ushort ReadWord(int deviceAddress, byte register)
    {
        if (deviceAddress is < 0x00 or > 0x7F)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceAddress),
                deviceAddress,
                "A 7-bit I2C device address (0x00–0x7F) is required.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var fd = EnsureOpen();
            SelectSlave(fd, deviceAddress);

            var pointer = new[] { register };
            if (Libc.Write(fd, pointer, (nuint)pointer.Length) != 1)
            {
                throw Failure($"Failed to write I2C register pointer 0x{register:X2} on '{_busPath}'");
            }

            var buffer = new byte[2];
            if (Libc.Read(fd, buffer, (nuint)buffer.Length) != 2)
            {
                throw Failure($"Failed to read I2C word from register 0x{register:X2} on '{_busPath}'");
            }

            return (ushort)((buffer[0] << 8) | buffer[1]);
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
            if (_fd >= 0)
            {
                Libc.Close(_fd);
                _fd = -1;
            }
        }
    }

    private int EnsureOpen()
    {
        if (_fd >= 0)
        {
            return _fd;
        }

        var fd = Libc.Open(_busPath, ORdwr);
        if (fd < 0)
        {
            throw Failure($"Failed to open I2C bus '{_busPath}'");
        }

        _fd = fd;
        return fd;
    }

    private void SelectSlave(int fd, int deviceAddress)
    {
        if (Libc.Ioctl(fd, I2cSlave, deviceAddress) < 0)
        {
            throw Failure($"Failed to select I2C slave 0x{deviceAddress:X2} on '{_busPath}'");
        }
    }

    private static InvalidOperationException Failure(string message)
    {
        var errno = Marshal.GetLastPInvokeError();
        return new InvalidOperationException(
            $"{message}. errno={errno} ({Marshal.GetPInvokeErrorMessage(errno)})");
    }

    private static class Libc
    {
        private const string Library = "libc";

        [DllImport(Library, EntryPoint = "open", SetLastError = true)]
        internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport(Library, EntryPoint = "close", SetLastError = true)]
        internal static extern int Close(int fd);

        [DllImport(Library, EntryPoint = "ioctl", SetLastError = true)]
        internal static extern int Ioctl(int fd, ulong request, int arg);

        [DllImport(Library, EntryPoint = "write", SetLastError = true)]
        internal static extern nint Write(int fd, byte[] buffer, nuint count);

        [DllImport(Library, EntryPoint = "read", SetLastError = true)]
        internal static extern nint Read(int fd, [Out] byte[] buffer, nuint count);
    }
}

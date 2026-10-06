namespace DroneControl.Infrastructure;

/// <summary>
/// Lowest I2C seam: 16-bit word reads from a device register on the configured
/// bus. The concrete implementation (<see cref="RaspberryI2cBus"/>) operates
/// <c>/dev/i2c-N</c>; tests supply a fake. No device-specific knowledge exists
/// at this seam — no register meanings, no addresses, no sensor vocabulary.
/// </summary>
public interface II2cBus : IDisposable
{
    /// <summary>
    /// Reads a 16-bit big-endian word from device register
    /// <paramref name="register"/>. The one-byte register pointer write that
    /// every read requires is part of this operation. Throws
    /// <see cref="InvalidOperationException"/> with the underlying errno reason
    /// when the bus is missing or not accessible.
    /// </summary>
    ushort ReadWord(int deviceAddress, byte register);
}

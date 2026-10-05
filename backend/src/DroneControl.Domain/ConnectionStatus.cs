namespace DroneControl.Domain;

/// <summary>Connection lifecycle of a system component.</summary>
public enum ConnectionStatus
{
    /// <summary>Not connected.</summary>
    Offline,

    /// <summary>Connection attempt in progress.</summary>
    Connecting,

    /// <summary>Connected.</summary>
    Connected,

    /// <summary>Connection failed.</summary>
    Error
}

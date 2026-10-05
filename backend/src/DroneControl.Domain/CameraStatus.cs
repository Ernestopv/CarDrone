namespace DroneControl.Domain;

/// <summary>Camera stream states.</summary>
public enum CameraStatus
{
    /// <summary>Camera unavailable or drone disconnected.</summary>
    Offline,

    /// <summary>Stream connection in progress.</summary>
    Connecting,

    /// <summary>Live stream active.</summary>
    Streaming,

    /// <summary>Camera or stream failed.</summary>
    Error
}

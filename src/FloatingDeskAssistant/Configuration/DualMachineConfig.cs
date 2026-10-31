namespace FloatingDeskAssistant.Configuration;

public enum DualMachineRole { Data, Display }

public sealed class DualMachineConfig
{
    public bool Enabled { get; set; }
    public DualMachineRole Role { get; set; } = DualMachineRole.Data;
    public int Port { get; set; } = 38657;
    public string PeerOffer { get; set; } = "";
    public bool IsData => Enabled && Role == DualMachineRole.Data;
    public bool IsDisplay => Enabled && Role == DualMachineRole.Display;

    public DualMachineConfig Clone() => new()
    {
        Enabled = Enabled,
        Role = Enum.IsDefined(Role) ? Role : DualMachineRole.Data,
        Port = Port is >= 1024 and <= 65535 ? Port : 38657,
        PeerOffer = PeerOffer?.Trim() ?? ""
    };
}

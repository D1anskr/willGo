namespace FloatingDeskAssistant.Configuration;

public enum DualMachineRole { Data, Display }
public enum FusionStyle { Dark, Light, Amber }
public enum FusionBackdrop { Black, White }

public sealed class DualMachineConfig
{
    public bool Enabled { get; set; }
    public DualMachineRole Role { get; set; } = DualMachineRole.Data;
    public int Port { get; set; } = 38657;
    public string PeerOffer { get; set; } = "";
    public bool FusionDisplayEnabled { get; set; }
    public FusionStyle FusionStyle { get; set; } = FusionStyle.Dark;
    public FusionBackdrop FusionBackdrop { get; set; } = FusionBackdrop.Black;
    public double FusionOpacity { get; set; } = 1;
    public double FusionAnswerFontSize { get; set; } = 22;
    public bool IsData => Enabled && Role == DualMachineRole.Data;
    public bool IsDisplay => Enabled && Role == DualMachineRole.Display;
    public bool IsFusionDisplay => IsDisplay && FusionDisplayEnabled;

    public DualMachineConfig Clone() => new()
    {
        Enabled = Enabled,
        Role = Enum.IsDefined(Role) ? Role : DualMachineRole.Data,
        Port = Port is >= 1024 and <= 65535 ? Port : 38657,
        PeerOffer = PeerOffer?.Trim() ?? "",
        FusionDisplayEnabled = FusionDisplayEnabled,
        FusionStyle = Enum.IsDefined(FusionStyle) ? FusionStyle : FusionStyle.Dark,
        FusionBackdrop = Enum.IsDefined(FusionBackdrop) ? FusionBackdrop : FusionBackdrop.Black,
        FusionOpacity = double.IsFinite(FusionOpacity) ? Math.Clamp(FusionOpacity, .3, 1) : 1,
        FusionAnswerFontSize = double.IsFinite(FusionAnswerFontSize) ? Math.Clamp(FusionAnswerFontSize, 14, 28) : 22
    };
}

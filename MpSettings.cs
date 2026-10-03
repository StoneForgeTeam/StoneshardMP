using StoneForge;

namespace StoneshardMP;

// The mod's settings (on its page in the Mods window, and in the Multiplayer window): who we are, where to join,
// and the host's port and player limit.
public sealed class MpSettings
{
    public TextSetting Name { get; }
    public TextSetting JoinAddress { get; }
    public SliderSetting Port { get; }
    public SliderSetting MaxPlayers { get; }
    public ToggleSetting ShowNames { get; }

    public MpSettings(ModSettings settings)
    {
        Name = settings.Text("name", "Name", "", maxLength: 32, tooltip: "Your name to the other players (empty: your Steam name).");
        JoinAddress = settings.Text("joinAddress", "Join address", "127.0.0.1", maxLength: 64, tooltip: "The host's IP address or name, to join their game.");
        Port = settings.Slider("port", "Port", 7777, min: 1024, max: 65535, step: 1, tooltip: "The UDP port the host listens on (the host forwards it on their router to be reached over the internet).");
        Port.Format = value => $"{value:0}";
        MaxPlayers = settings.Slider("maxPlayers", "Max players", 4, min: 2, max: Net.Session.MaxPlayers, step: 1, tooltip: "Hosting: how many players, you included, can be in your game.");
        MaxPlayers.Format = value => $"{value:0}";
        ShowNames = settings.Toggle("showNames", "Name tags", true, "Other players' names over their characters.");
    }
}

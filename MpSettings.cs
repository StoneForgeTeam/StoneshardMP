using StoneForge;

namespace StoneshardMP;

// The mod's settings (on its page in the Mods window): who we are, and the host's port and player limit. Where to join
// is asked by the Join Game dialog, which keeps the last address typed (JoinAddress: not on the page).
public sealed class MpSettings
{
    public TextSetting Name { get; }
    public TextSetting JoinAddress { get; }
    public NumberSetting Port { get; }
    public SliderSetting MaxPlayers { get; }
    public ToggleSetting ShowNames { get; }
    public ToggleSetting PartyFrames { get; }
    // (The party frames slid away - their tab; not on the settings page.)
    public ToggleSetting PartyHidden { get; }

    public MpSettings(ModSettings settings)
    {
        Name = settings.Text("name", "Name", "", maxLength: 32, tooltip: "Your name to the other players (empty: your Steam name).");
        JoinAddress = settings.Text("joinAddress", "Join address", "127.0.0.1", maxLength: 64, tooltip: "The host's IP address or name, to join their game.");
        JoinAddress.Visible = false;
        // (Typed - digits only - and a port: 1 to 65535, what a UDP port can be.)
        Port = settings.Number("port", "Port", 7777, min: 1, max: ushort.MaxValue, tooltip: "The UDP port the host listens on (the host forwards it on their router to be reached over the internet).");
        MaxPlayers = settings.Slider("maxPlayers", "Max players", 4, min: 2, max: Net.Session.MaxPlayers, step: 1, tooltip: "Hosting: how many players, you included, can be in your game.");
        MaxPlayers.Format = value => $"{value:0}";
        ShowNames = settings.Toggle("showNames", "Name tags", true, "Other players' names over their characters.");
        PartyFrames = settings.Toggle("partyFrames", "Party frames", true, "A frame for each other player at the right of the screen: health, energy, level, status effects and which way they are.");
        PartyHidden = settings.Toggle("partyHidden", "Party frames hidden", false);
        PartyHidden.Visible = false;
    }
}

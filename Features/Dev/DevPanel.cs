using System;
using System.Collections.Generic;
using System.Linq;
using StoneForge;

namespace StoneshardMP.Features.Dev;

// Dev tools' window: tabs along the top, the open tab's lines (scrolled with the wheel), and its buttons at the bottom.
// What's in each tab - its lines and buttons - is DevTools'; this only shows it, at the right edge of the screen.
public sealed class DevPanel : UIElement
{
    public sealed record Line(string Text, int Colour);
    public sealed record Action(string Label, System.Action Run, string? Tooltip = null);

    private const double PanelWidth = 400, PanelHeight = 360, Pad = 8, TabHeight = 18, LineHeight = 11, ButtonHeight = 18;
    private const int ButtonsPerRow = 3, RefreshFrames = 10;
    private static readonly int Gold = Draw.Rgb(214, 186, 120);

    private readonly IReadOnlyList<string> _tabs;
    private readonly Func<int, List<Line>> _lines;
    private readonly Func<int, List<Action>> _actions;
    private readonly List<UIButton> _tabButtons = new();
    private readonly List<UIButton> _actionButtons = new();
    private List<Line> _shown = new();
    private string _actionsShown = "";
    private int _tab, _scroll, _frame;

    public DevPanel(IReadOnlyList<string> tabs, Func<int, List<Line>> lines, Func<int, List<Action>> actions)
    {
        _tabs = tabs;
        _lines = lines;
        _actions = actions;
        Anchor = UIAnchor.Right;
        X = -12;
        Y = 0;
        Width = PanelWidth;
        Height = PanelHeight;
        Visible = false;
        double tabWidth = (PanelWidth - Pad * 2) / tabs.Count;
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var button = Add(new UIButton(tabs[i], Pad + i * tabWidth, Pad + 12, tabWidth - 2, TabHeight, () => Open(index)));
            _tabButtons.Add(button);
        }
    }

    public int Tab => _tab;

    /// <summary>The lines as shown now (the Copy button's).</summary>
    public IEnumerable<string> ShownText => _shown.Select(l => l.Text);

    public void Open(int tab)
    {
        _tab = Math.Clamp(tab, 0, _tabs.Count - 1);
        _scroll = 0;
        _frame = 0;
    }

    private double LinesTop => Pad + 12 + TabHeight + 6;
    private int Rows => (int)Math.Ceiling(_actionButtons.Count / (double)ButtonsPerRow);
    private double LinesBottom => Height - Pad - Rows * (ButtonsPerRow > 0 ? ButtonHeight + 3 : 0) - 4;
    private int VisibleLines => Math.Max(1, (int)((LinesBottom - LinesTop) / LineHeight));

    protected override void OnUpdate(double deltaTime)
    {
        if (_frame++ % RefreshFrames != 0)
            return;
        try { _shown = _lines(_tab); }
        catch (Exception e) { _shown = new() { new Line("This tab failed: " + e.Message, Draw.Rgb(230, 90, 70)) }; }
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _shown.Count - VisibleLines));
        List<Action> actions;
        try { actions = _actions(_tab); }
        catch (Exception e) { actions = new(); DevLog.Add("Dev tools: the tab's buttons failed: " + e.Message); }
        // (The buttons made again when they change - a toggle's label, another tab's.)
        string signature = _tab + "|" + string.Join("|", actions.Select(a => a.Label));
        if (signature == _actionsShown)
        {
            for (int i = 0; i < actions.Count; i++)
                _actionButtons[i].Tooltip = actions[i].Tooltip;
            return;
        }
        _actionsShown = signature;
        foreach (var button in _actionButtons)
            Remove(button);
        _actionButtons.Clear();
        double width = (PanelWidth - Pad * 2) / ButtonsPerRow;
        int rows = (int)Math.Ceiling(actions.Count / (double)ButtonsPerRow);
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            double x = Pad + i % ButtonsPerRow * width, y = Height - Pad - (rows - i / ButtonsPerRow) * (ButtonHeight + 3) + 3;
            var button = Add(new UIButton(action.Label, x, y, width - 3, ButtonHeight, () => Run(action)) { Tooltip = action.Tooltip });
            _actionButtons.Add(button);
        }
    }

    private void Run(Action action)
    {
        try { action.Run(); }
        catch (Exception e) { DevLog.Add($"{action.Label} failed: {e.Message}"); }
        // (Its effect shown straight away.)
        _frame = 0;
        _actionsShown = "";
    }

    protected override bool OnWheel(int delta)
    {
        _scroll = Math.Clamp(_scroll - delta * 3, 0, Math.Max(0, _shown.Count - VisibleLines));
        return true;
    }

    protected override void OnDraw(double x, double y)
    {
        Draw.Frame(x, y, Width, Height);
        Draw.Text(x + Pad, y + Pad, "StoneshardMP dev tools", Gold);
        Draw.Text(x + Width - Pad, y + Pad, "Ctrl+Shift+M", Draw.Muted, Draw.AlignRight);
        for (int i = 0; i < _tabButtons.Count; i++)
            if (i == _tab)
            {
                var tab = _tabButtons[i];
                Draw.Rectangle(x + tab.X, y + tab.Y + TabHeight, x + tab.X + tab.Width, y + tab.Y + TabHeight + 1, Gold);
            }
        int visible = VisibleLines;
        double top = y + LinesTop;
        for (int i = 0; i < visible && _scroll + i < _shown.Count; i++)
        {
            var line = _shown[_scroll + i];
            string text = line.Text;
            while (text.Length > 4 && Draw.TextWidth(text) > Width - Pad * 2 - 6)
                text = text[..^2] + ".";
            Draw.Text(x + Pad, top + i * LineHeight, text, line.Colour);
        }
        if (_shown.Count > visible)
        {
            // (Where in the lines we are.)
            double track = LinesBottom - LinesTop, thumb = Math.Max(10, track * visible / _shown.Count);
            double at = track - thumb == 0 ? 0 : (track - thumb) * _scroll / Math.Max(1, _shown.Count - visible);
            Draw.Rectangle(x + Width - Pad - 2, top + at, x + Width - Pad, top + at + thumb, Draw.Muted);
        }
    }
}

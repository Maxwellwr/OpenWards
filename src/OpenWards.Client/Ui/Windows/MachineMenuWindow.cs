// The machines list, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/machine_menu.lua (UIMachineMenu: its panels,
// colours, sizes, draw, scrollbar and buttons) and window.lua (Scrollbar:setRange and setXorY): every machine of the
// player's hospital with its danger and repair marks, its status and strength, sortable by each.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Sim;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;

namespace OpenWards.Client.Ui.Windows;

/// <summary>How the machines list is sorted (<c>sort_method</c>).</summary>
internal enum MachineSort
{
    /// <summary><c>sortByRatio</c>: smoking machines first, the fewest uses left among them first, then the lowest share left.</summary>
    Ratio,

    /// <summary><c>sortByName</c>.</summary>
    Name,

    /// <summary><c>sortByRemain</c>: the fewest uses left first.</summary>
    Remaining,

    /// <summary><c>sortByStrength</c>: the weakest first.</summary>
    Strength,
}

/// <summary>A machine as the list shows it (<c>machineForList</c>).</summary>
internal sealed record MachineListEntry(RoomObject Machine, string Name, bool Smoking, int AssignedHandymanId, int Strength, int Remaining, int Percentage);

/// <summary>
/// <c>UIMachineMenu</c> (F10): the player's machines in rooms that have not crashed. "!" marks a machine at risk of
/// exploding (a click offers to replace it), "X" one a handyman is on the way to repair (a click opens his window); the
/// name opens the machine's window and moves the view there. With CorsixTH's sprites it is drawn as CorsixTH draws it:
/// an orange window of bevelled panels, a row of 32 pixels for each machine with its status bar, a scrollbar and the
/// original's close button; else as a table.
/// </summary>
internal sealed class MachineMenuWindow : UiWindow
{
    private const int RowHeight = 32;
    private const int Margin = 15;
    private const int Width = 480;
    private const int IndicatorWidth = 32;
    private const int NameWidth = 180;
    private const int StatusWidth = 112;

    // The colours of the window and its panels (col_bg and the others at the top of machine_menu.lua).
    private static readonly Color Background = new(219, 81, 12);
    private static readonly Color ShadowColour = new(202, 69, 8);
    private static readonly Color HeaderColour = new(232, 95, 16);
    private static readonly Color AssignedButton = new(84, 200, 84);
    private static readonly Color ShadedAssignedButton = new(92, 150, 92);
    private static readonly Color DisabledAssignedButton = new(84, 112, 84);
    private static readonly Color WarningButton = new(219, 36, 36);
    private static readonly Color DisabledColour = new(159, 90, 56);

    /// <summary>The close button: Req03V's sprite 337, 338 while pressed (<c>addPanel(337, 446, 15):makeButton(0, 0, 24, 24, 338, ...)</c>).</summary>
    private static readonly Rectangle CloseButton = new(446, Margin, 24, 24);

    private readonly ResizablePanels _panels = new();
    private int? _rows;
    private int _value = 1;
    private int? _dragOffset;
    private bool _closePressed;

    public MachineSort Sort { get; private set; } = MachineSort.Ratio;

    public override string Title => UiText.Get("openwards.toolbar.machines", "Machines");

    public override string? Hotkey => "F10";

    /// <summary>How many machines the window shows at once.</summary>
    public int VisibleRows => _rows ?? 4;

    /// <summary>The first machine shown, from 1 (the scrollbar's <c>value</c>).</summary>
    public int ScrollValue => _value;

    /// <summary>CorsixTH's look where its sprites, the game's font and the machine window's panels are there.</summary>
    public override bool IsFullscreen(IWindowHost host) =>
        ResizablePanels.IsAvailable(host) && host.Graphics!.Sheet("REQ03V", OriginalGraphics.MainPalette) is not null;

    public override Point FullscreenSize => new(Width, WindowHeight(VisibleRows));

    /// <summary>
    /// The rows are worked out once, as the window opens: a quarter of the screen's height in pixels over the row height,
    /// rounded up (kept on the screen here, where a big UI scale could push it off).
    /// </summary>
    public override Point FullscreenSizeOn(Rectangle screen)
    {
        _rows ??= RowsFor(screen.Height, Rendering.ScreenCanvas.Scale);
        return FullscreenSize;
    }

    /// <summary><c>setDefaultPosition(0.05, 0.05)</c>.</summary>
    public override Vector2? FullscreenPosition => new(0.05f, 0.05f);

    public override bool CapturesMouse => _dragOffset is not null;

    /// <summary>
    /// How many rows the window has on a screen <paramref name="height"/> high in UI pixels drawn <paramref name="scale"/>
    /// times: <c>math.ceil(math.floor(app.config.height / 4) / row_height)</c>, at least one and no more than fit.
    /// </summary>
    public static int RowsFor(int height, int scale)
    {
        var rows = (int)Math.Ceiling(Math.Floor(height * Math.Max(1, scale) / 4.0) / RowHeight);
        return Math.Max(1, Math.Min(rows, (height - WindowHeight(0)) / RowHeight));
    }

    /// <summary>The header, the rows and a margin above and below.</summary>
    private static int WindowHeight(int rows) => RowHeight + (rows * RowHeight) + (Margin * 2);

    /// <summary>The scrollbar's base (<c>addColourPanel(446, window_margin + 30, 24, row_height * rows)</c>).</summary>
    private Rectangle ScrollbarBase => new(446, Margin + 30, 24, RowHeight * VisibleRows);

    /// <summary>
    /// <c>Scrollbar:setRange(1, max(1, #machines), rows, value)</c>: the value kept within the range and the slider, a
    /// bevelled panel inside the base, as long as the share of the machines shown and as far down as the value.
    /// </summary>
    public static (int Value, Rectangle Slider) ScrollbarRange(Rectangle bar, int count, int rows, int value)
    {
        var max = Math.Max(1, count);
        var page = Math.Min(rows, max);
        value = Math.Min(value, Math.Max(1, max - page + 1));
        int minY = bar.Y + 1, maxHeight = bar.Height - 2;
        var height = (int)Math.Ceiling((double)page / max * maxHeight);
        var maxY = minY + maxHeight - height;
        var y = (int)Math.Floor(((double)(value - 1) / (max - page + 1) * (maxY - minY)) + minY);
        return (value, new Rectangle(bar.X + 1, y, bar.Width - 2, height));
    }

    /// <summary><c>Scrollbar:setXorY</c>: the slider dragged to <paramref name="y"/>, kept on the bar, gives the value.</summary>
    public static int ScrollbarValueAt(Rectangle bar, int count, int rows, int y)
    {
        var max = Math.Max(1, count);
        var page = Math.Min(rows, max);
        var slider = ScrollbarRange(bar, count, rows, 1).Slider;
        int minY = bar.Y + 1, maxY = minY + (bar.Height - 2) - slider.Height;
        y = Math.Clamp(y, minY, maxY);
        return (int)Math.Floor((double)(y - minY) / (maxY - minY + 1) * (max - page + 1)) + 1;
    }

    /// <summary>The panels: the header, then a row for each place on the window, filled from the machines scrolled to.</summary>
    private List<DialogPanel> Panels(IWindowHost host, List<MachineListEntry> machines)
    {
        string Sorted(string key, string fallback) => UiText.Get(key, fallback) + " " + UiText.Get("tooltip.machine_menu.sort", "Click to sort by this value.");
        Action<int> SortBy(MachineSort sort) => _ => Sort = sort;

        var panels = new List<DialogPanel>();
        var x = Margin;
        panels.Add(new DialogPanel(new Rectangle(x, Margin, IndicatorWidth, RowHeight), "!", HeaderColour, Tooltip: UiText.Get("tooltip.machine_menu.header.smoking", "Danger Indicator")));
        x += IndicatorWidth;
        panels.Add(new DialogPanel(new Rectangle(x, Margin, IndicatorWidth, RowHeight), "X", HeaderColour, Tooltip: UiText.Get("tooltip.machine_menu.header.assigned", "Repair assignment Indicator")));
        x += IndicatorWidth;
        panels.Add(new DialogPanel(new Rectangle(x, Margin, NameWidth, RowHeight), UiText.Get("machine_menu.machine", "Machine"), HeaderColour, Click: SortBy(MachineSort.Name), Tooltip: UiText.Get("tooltip.machine_menu.header.machine", "Machine name")));
        x += NameWidth;
        panels.Add(new DialogPanel(new Rectangle(x, Margin, StatusWidth, RowHeight), UiText.Get("machine_menu.status", "Status"), HeaderColour, Click: SortBy(MachineSort.Ratio), Tooltip: Sorted("tooltip.machine_menu.header.status", "Status of the machines.")));
        x += StatusWidth;
        panels.Add(new DialogPanel(new Rectangle(x, Margin, IndicatorWidth, RowHeight), null, HeaderColour, Click: SortBy(MachineSort.Remaining), Tooltip: Sorted("tooltip.machine_menu.header.remaining_strength", "Remaining Strength of the machines.")));
        x += IndicatorWidth;
        panels.Add(new DialogPanel(new Rectangle(x, Margin, IndicatorWidth, RowHeight), null, HeaderColour, Click: SortBy(MachineSort.Strength), Tooltip: Sorted("tooltip.machine_menu.header.total_strength", "Total Strength of the machines.")));

        for (var i = 1; i <= VisibleRows; i++)
        {
            var y = Margin + (RowHeight * i);
            var entry = _value + i - 2 < machines.Count ? machines[_value + i - 2] : null;
            x = Margin;
            Rectangle Next(int width)
            {
                var area = new Rectangle(x, y, width, RowHeight);
                x += width;
                return area;
            }

            if (entry is null)
            {
                // scrollbarMoved for an empty row: every panel in the disabled colours, no labels, no buttons.
                panels.Add(new DialogPanel(Next(IndicatorWidth), null, DisabledColour, Tooltip: UiText.Get("tooltip.machine_menu.smoking", "This box is marked if the machine is at risk of exploding. Click to buy a new machine.")));
                panels.Add(new DialogPanel(Next(IndicatorWidth), null, DisabledAssignedButton, Tooltip: UiText.Get("tooltip.machine_menu.assigned", "This box is marked if a handyman is assigned to fix corresponding machine. Click to show assigned handyman.")));
                panels.Add(new DialogPanel(Next(NameWidth), null, DisabledColour, Tooltip: UiText.Get("tooltip.machine_menu.machine", "List of machines - click machine to open machine's window and scroll to its location")));
                panels.Add(new DialogPanel(Next(StatusWidth), null, DisabledColour, Tooltip: OriginalTooltips.Get(host, 116, "Status")));
                panels.Add(new DialogPanel(Next(IndicatorWidth), null, DisabledColour, Tooltip: UiText.Get("tooltip.machine_menu.remaining_strength", "This value shows remaining strength of the machine")));
                panels.Add(new DialogPanel(Next(IndicatorWidth), null, DisabledColour, Tooltip: UiText.Get("tooltip.machine_menu.total_strength", "This value shows total strength of the machine")));
                continue;
            }

            var machine = entry.Machine;
            var assigned = entry.AssignedHandymanId;
            panels.Add(new DialogPanel(Next(IndicatorWidth), entry.Smoking ? "!" : string.Empty, entry.Smoking ? WarningButton : Background, Click: _ => WarningClicked(host, machine),
                Tooltip: UiText.Get("tooltip.machine_menu.smoking", "This box is marked if the machine is at risk of exploding. Click to buy a new machine.")));
            panels.Add(new DialogPanel(Next(IndicatorWidth), assigned != 0 ? "X" : string.Empty, assigned != 0 ? AssignedButton : ShadedAssignedButton, Click: assigned != 0 ? _ => AssignedClicked(host, machine, assigned) : null,
                Tooltip: UiText.Get("tooltip.machine_menu.assigned", "This box is marked if a handyman is assigned to fix corresponding machine. Click to show assigned handyman.")));
            panels.Add(new DialogPanel(Next(NameWidth), " " + entry.Name, Background, LeftAligned: true, Click: _ =>
            {
                // scrollToEntity: the view moves to the machine and its window opens.
                host.CenterOn(machine.Tile);
                host.Open(new MachineWindow(machine.Id));
            }, Tooltip: UiText.Get("tooltip.machine_menu.machine", "List of machines - click machine to open machine's window and scroll to its location")));
            panels.Add(new DialogPanel(Next(StatusWidth), null, Background, Tooltip: OriginalTooltips.Get(host, 116, "Status")));
            panels.Add(new DialogPanel(Next(IndicatorWidth), entry.Remaining.ToString(CultureInfo.InvariantCulture), Background, Tooltip: UiText.Get("tooltip.machine_menu.remaining_strength", "This value shows remaining strength of the machine")));
            panels.Add(new DialogPanel(Next(IndicatorWidth), entry.Strength.ToString(CultureInfo.InvariantCulture), Background, Tooltip: UiText.Get("tooltip.machine_menu.total_strength", "This value shows total strength of the machine")));
        }

        return panels;
    }

    /// <summary><c>warningButtonClicked</c>: in a working room, the machine's window with the offer to replace it.</summary>
    private static void WarningClicked(IWindowHost host, RoomObject machine)
    {
        if (host.Simulation.FindEntity(machine.RoomId) is Room { IsActive: true })
        {
            var window = new MachineWindow(machine.Id);
            host.Open(window);
            window.AskToReplace(host, machine);
        }
    }

    /// <summary><c>assignedHandymanButtonClicked</c>: in a working room, the window of the handyman on his way.</summary>
    private static void AssignedClicked(IWindowHost host, RoomObject machine, int handyman)
    {
        if (host.Simulation.FindEntity(machine.RoomId) is Room { IsActive: true })
        {
            host.Open(new StaffWindow(handyman));
        }
    }

    /// <summary><c>UIMachineMenu:update</c>: the machines sorted, and the scrollbar's value kept within them.</summary>
    private List<MachineListEntry> Update(IWindowHost host)
    {
        var machines = List(host.Simulation, host.Player, Sort);
        _value = ScrollbarRange(ScrollbarBase, machines.Count, VisibleRows, _value).Value;
        return machines;
    }

    /// <summary>
    /// <c>UIMachineMenu:draw</c>: the window and its panels, the sort icons over the two narrow headers, then for each
    /// machine shown the status icon and the bar of what is left of its strength; the scrollbar and the close button.
    /// </summary>
    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var machines = Update(host);
        _panels.Draw(batch, host, origin, FullscreenSize, Panels(host, machines), mouse, Background);
        var graphics = host.Graphics!;
        var look = graphics.Resizable!;
        var sheet = graphics.Sheet("REQ03V", OriginalGraphics.MainPalette)!;
        look.DrawAux(batch, 25, origin.X + 374, origin.Y + Margin + 2);
        look.DrawAux(batch, 26, origin.X + 407, origin.Y + Margin + 2);
        for (var i = 1; i <= VisibleRows; i++)
        {
            if (_value + i - 2 >= machines.Count)
            {
                continue;
            }

            var state = machines[_value + i - 2].Machine.Machine!;
            look.DrawAux(batch, 24, origin.X + 264, origin.Y + Margin + (RowHeight * i) + 3);
            var bar = MachineScreen.StatusWidth(state.TimesUsed, state.Strength);
            for (var dx = 0; dx < bar; dx++)
            {
                sheet.Draw(batch, 352, origin.X + 293 + dx, origin.Y + Margin + (RowHeight * i) + (RowHeight / 2) - 2, Color.White);
            }
        }

        var track = ScrollbarBase;
        look.Fill(batch, new Rectangle(origin + track.Location, track.Size), ShadowColour);
        var slider = ScrollbarRange(track, machines.Count, VisibleRows, _value).Slider;
        look.DrawBevel(batch, new Rectangle(origin + slider.Location, slider.Size), Background, lowered: false);

        var local = new Point(mouse.X, mouse.Y) - origin;
        var pressed = _closePressed && mouse.LeftButton == ButtonState.Pressed && CloseButton.Contains(local);
        sheet.Draw(batch, pressed ? 338 : 337, origin.X + CloseButton.X, origin.Y + CloseButton.Y, Color.White);
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        var machines = Update(host);
        var leftDown = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var leftUp = mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed;
        var track = ScrollbarBase;
        var slider = ScrollbarRange(track, machines.Count, VisibleRows, _value).Slider;
        if (_dragOffset is { } offset)
        {
            // The slider follows the mouse while the button is held.
            _value = ScrollbarValueAt(track, machines.Count, VisibleRows, local.Y - offset);
            if (leftUp)
            {
                _dragOffset = null;
            }

            return;
        }

        if (leftDown && slider.Contains(local))
        {
            _dragOffset = local.Y - slider.Y;
            return;
        }

        if (leftDown)
        {
            _closePressed = CloseButton.Contains(local);
        }
        else if (leftUp && _closePressed)
        {
            _closePressed = false;
            if (CloseButton.Contains(local))
            {
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Close(this);
                return;
            }
        }

        _panels.Mouse(host, Panels(host, machines), local, mouse, previous);
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) =>
        CloseButton.Contains(local)
            ? UiText.Get("tooltip.machine_menu.close", "Close the machine list dialog")
            : ResizablePanels.Tooltip(Panels(host, Update(host)), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) =>
        ResizablePanels.FindButton(Panels(host, Update(host)), text, nth);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var machines = List(host.Simulation, host.Player, Sort);
        yield return WindowRow.Of(
            new WindowCell("!", Dim: true) { Tooltip = UiText.Get("tooltip.machine_menu.header.smoking", "Danger indicator") },
            new WindowCell("X", Dim: true) { Tooltip = UiText.Get("tooltip.machine_menu.header.assigned", "Repair assignment indicator") },
            Header(UiText.Get("machine_menu.machine", "Machine"), MachineSort.Name, UiText.Get("tooltip.machine_menu.header.machine", "Machine name")),
            Header(UiText.Get("machine_menu.status", "Status"), MachineSort.Ratio, UiText.Get("tooltip.machine_menu.header.ratio", "Remaining strength to total strength of the machines")),
            Header(UiText.Get("machine_menu.remaining_strength", "Remain"), MachineSort.Remaining, UiText.Get("tooltip.machine_menu.header.remaining_strength", "Remaining strength of the machines")),
            Header(UiText.Get("machine_menu.total_strength", "Strength"), MachineSort.Strength, UiText.Get("tooltip.machine_menu.header.total_strength", "Total strength of the machines")));
        if (machines.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("openwards.machines.none", "The hospital has no machines."), dim: true);
            yield break;
        }

        foreach (var entry in machines)
        {
            var machine = entry.Machine;
            var assigned = entry.AssignedHandymanId;
            yield return WindowRow.Of(
                WindowCell.Button(entry.Smoking ? "!" : " ", () => host.Open(new MachineWindow(machine.Id)), enabled: entry.Smoking, highlight: entry.Smoking),
                WindowCell.Button(assigned != 0 ? "X" : " ", () => host.Open(new StaffWindow(assigned)), enabled: assigned != 0),
                WindowCell.Button(entry.Name, () =>
                {
                    host.CenterOn(machine.Tile);
                    host.Open(new MachineWindow(machine.Id));
                }),
                WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{entry.Percentage}%")),
                WindowCell.Number(entry.Remaining.ToString(CultureInfo.InvariantCulture), highlight: entry.Smoking),
                WindowCell.Number(entry.Strength.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private WindowCell Header(string text, MachineSort sort, string tooltip) =>
        WindowCell.Button(text, () => Sort = sort, highlight: Sort == sort) with { Tooltip = tooltip.TrimEnd('.') + ". " + UiText.Get("tooltip.machine_menu.sort", "Click to sort by this value.") };

    /// <summary><c>UIMachineMenu:update</c>: the player's machines (not slave parts) in rooms that have not crashed, sorted.</summary>
    public static List<MachineListEntry> List(Simulation simulation, int player, MachineSort sort)
    {
        var assigned = new Dictionary<int, int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: HandymanBehaviour { TargetId: not 0 } handyman, IsFired: false } staff
                && handyman.Phase is HandymanPhase.WalkingToMachine or HandymanPhase.WaitingToRepair or HandymanPhase.GoingToRepair or HandymanPhase.Repairing)
            {
                assigned.TryAdd(handyman.TargetId, staff.Id);
            }
        }

        var list = new List<MachineListEntry>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is not RoomObject { Machine: { } state, IsDestroyed: false } machine
                || simulation.FindEntity(machine.RoomId) is not Room { IsCrashed: false } room
                || RoomVisits.Owner(simulation, room) != player)
            {
                continue;
            }

            var remaining = state.RemainingUses;
            var percentage = state.Strength <= 0 ? 0 : (int)Math.Floor(remaining * 100.0 / state.Strength);
            var name = UiText.ObjectName(simulation.Content, machine.ObjectType);
            list.Add(new MachineListEntry(machine, name, state.IsBreaking, assigned.GetValueOrDefault(machine.Id), state.Strength, remaining, percentage));
        }

        // Lua's table.sort is not stable; ties keep the order the machines were built in here.
        Comparison<MachineListEntry> compare = sort switch
        {
            MachineSort.Name => (a, b) => string.CompareOrdinal(a.Name, b.Name),
            MachineSort.Remaining => (a, b) => a.Remaining.CompareTo(b.Remaining),
            MachineSort.Strength => (a, b) => a.Strength.CompareTo(b.Strength),
            _ => CompareByRatio,
        };
        return [.. list.Select((entry, index) => (entry, index)).OrderBy(pair => pair.entry, Comparer<MachineListEntry>.Create(compare)).ThenBy(pair => pair.index).Select(pair => pair.entry)];
    }

    private static int CompareByRatio(MachineListEntry a, MachineListEntry b)
    {
        if (a.Smoking != b.Smoking)
        {
            return a.Smoking ? -1 : 1;
        }

        if (a.Smoking && a.Remaining != b.Remaining)
        {
            return a.Remaining.CompareTo(b.Remaining);
        }

        return a.Percentage.CompareTo(b.Percentage);
    }
}

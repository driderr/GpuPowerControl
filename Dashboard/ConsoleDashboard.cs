using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Renders a live-updating console dashboard using Spectre.Console.
/// Consumes IDashboardDataProvider for all data (decoupled from controller).
/// </summary>
public class ConsoleDashboard : IDisposable
{
    private readonly IDashboardDataProvider _provider;
    private readonly IAnsiConsole _console;
    private volatile bool _isRunning;
    // Use long for Interlocked operations (Interlocked.Read requires ref long)
    private long _showLogFlag = 1;
    private long _showConfigFlag;
    private long _jsonEnabledFlag;
    private volatile bool _disposed;
    private Thread? _renderThread;

    public ConsoleDashboard(IDashboardDataProvider provider)
        : this(provider, AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Out),
            Interactive = InteractionSupport.Yes,
        }))
    {
    }

    /// <summary>Creates a dashboard with a specific console instance (for testing).</summary>
    /// <remarks>M1: IAnsiConsole injection for testability.</remarks>
    public ConsoleDashboard(IDashboardDataProvider provider, IAnsiConsole console)
    {
        _provider = provider;
        _console = console;
    }

    /// <summary>Start the dashboard display.</summary>
    public void Start()
    {
        // Check if console output is redirected before rendering
        if (System.Console.IsOutputRedirected)
        {
            _console.WriteLine("[dim]Non-interactive console detected. Dashboard rendering disabled.[/]");
            return;
        }

        _isRunning = true;

        _renderThread = new Thread(() =>
        {
            // Live.Start() is blocking - the render loop runs inside its callback.
            // We use a mutable Rows object and call ctx.UpdateTarget() to swap it each frame.
            _console.Live(new Rows())
                .Overflow(VerticalOverflow.Visible)
                .Start(ctx =>
                {
                    while (_isRunning)
                    {
                        try
                        {
                            var renderables = RenderFrame();
                            ctx.UpdateTarget(new Rows(renderables));
                        }
                        catch (Exception ex)
                        {
                            ErrorConsole.Error($"Dashboard render failed: {ex.Message}");
                        }

                        Thread.Sleep(250);
                    }
                });
        })
        {
            IsBackground = true,
            Name = "DashboardRender"
        };
        _renderThread.Start();
    }

    private List<IRenderable> RenderFrame()
    {
        var config = _provider.Config;
        var current = _provider.Current;

        // Determine state color name for Style.Parse
        var stateColorName = current.IsControlling ? "yellow"
            : current.EventType == Core.ControllerEventType.Emergency ? "red"
            : current.EventType == Core.ControllerEventType.Stable ? "green"
            : "cyan";

        var renderables = new List<IRenderable>();

        // === HEADER ===
        renderables.Add(new Rule($"[bold green]GpuPowerControl[/] - {Markup.Escape(config.GpuName)}"));
        renderables.Add(new Markup($"[gray]Uptime: {FormatTimespan(current.Uptime)}[/]"));

        // === STATE & POWER ===
        // Use Panel widgets for colored bordered boxes, placed side by side with Columns
        var stateColor = stateColorName switch
        {
            "yellow" => Color.Yellow, "red" => Color.Red, "green" => Color.Green, _ => Color.Cyan
        };
        var statePanel = new Panel(new Text(current.IsControlling ? "CONTROLLING" : "IDLE"))
        {
            BorderStyle = new Style(stateColor),
            Border = BoxBorder.Rounded
        };

        var powerPanel = new Panel(new Text($"{current.CurrentPowerLimit}W / {config.MaxPower}W"))
        {
            BorderStyle = new Style(Color.Yellow),
            Border = BoxBorder.Rounded
        };

        // Use Grid with expanding first column + fixed second column
        // so the power box stays pinned to the same position regardless of state text length
        var statePowerGrid = new Grid();
        statePowerGrid.AddColumn();                          // expands left (default)
        statePowerGrid.AddColumn(new GridColumn { Width = 22 }); // fixed right
        statePowerGrid.AddRow(statePanel, powerPanel);
        renderables.Add(statePowerGrid);

        // === TEMPERATURE BAR ===
        renderables.Add(new Text("\n"));
        renderables.Add(new Markup($"Temp: [bold]{current.Temperature}C[/]  |  Target: {config.TargetTemp}C  |  Trigger: {config.TriggerTemp}C  |  Emergency: {config.EmergencyTemp}C"));
        renderables.Add(CreateTempBar(current.Temperature, config));

        // === POWER BAR ===
        renderables.Add(new Text("\n"));
        renderables.Add(new Markup($"Power: [bold]{current.CurrentPowerLimit}W[/]  |  Range: {config.MinPower}W - {config.MaxPower}W"));
        renderables.Add(CreatePowerBar(current, config));

        // === MAIN BODY: Grid with left (stats) and right (charts) side by side ===
        var derivColor = current.Derivative > 0 ? Color.Yellow : Color.Green;
        var derivStr = current.Derivative.ToString("+0.0;-0.0;0.0");

        // Build left column content: stats + PID + config + JSON status
        var leftContent = new List<IRenderable>();
        leftContent.Add(new Text("\n"));

        // Stats table
        var statsTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Padding(0, 0))
            .AddColumn(new TableColumn("").Padding(0, 0));
        statsTable.AddRow("Derivative:", $"[{derivColor}]{derivStr} C/s[/]");
        statsTable.AddRow("PID Cycles:", $"{current.PidCycles}");
        statsTable.AddRow("Transitions:", $"{current.StateTransitions}");
        statsTable.AddRow("Polling:", $"{current.PollingIntervalMs}ms");
        leftContent.Add(statsTable);

        // PID breakdown
        if (current.IsControlling && (current.PidP != 0 || current.PidI != 0 || current.PidD != 0))
        {
            leftContent.Add(new Text("\n"));
            leftContent.Add(new Markup("[bold]PID Breakdown:[/]"));
            var pidTable = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("").Padding(0, 0))
                .AddColumn(new TableColumn("").Padding(0, 0));
            pidTable.AddRow("P:", $"[magenta]{current.PidP:+0.0;-0.0;0.0}[/]");
            pidTable.AddRow("I:", $"[blue]{current.PidI:+0.0;-0.0;0.0}[/]");
            pidTable.AddRow("D:", $"[green]{current.PidD:+0.0;-0.0;0.0}[/]");
            pidTable.AddRow("Integral:", $"[gray]{current.PidIntegral:+0.0;-0.0;0.0}[/]");
            leftContent.Add(pidTable);
        }

        // Config table (toggleable)
        if (Interlocked.Read(ref _showConfigFlag) != 0)
        {
            leftContent.Add(new Text("\n"));
            var configTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Parameter")
                .AddColumn("Value");
            configTable.AddRow("Kp", config.Kp.ToString("F1"));
            configTable.AddRow("Ki", config.Ki.ToString("F1"));
            configTable.AddRow("Kd", config.Kd.ToString("F1"));
            configTable.AddRow("Target", $"{config.TargetTemp}C");
            configTable.AddRow("Trigger", $"{config.TriggerTemp}C");
            configTable.AddRow("Emergency", $"{config.EmergencyTemp}C");
            configTable.AddRow("Power Range", $"{config.MinPower}W - {config.MaxPower}W");
            leftContent.Add(configTable);
        }

        // JSON status
        leftContent.Add(new Text("\n"));
        var jsonLabel = Interlocked.Read(ref _jsonEnabledFlag) != 0 ? "[green]ON[/]" : "[gray]OFF[/]";
        leftContent.Add(new Markup($"JSON Publishing: {jsonLabel}  |  Read Failures: {current.ReadFailures}"));

        // Build right column content: history charts
        var history = _provider.GetHistory(80);
        IRenderable rightContent = history.Count >= 5 ? BuildHistoryColumn(history) : new Markup("[dim]Waiting for data...[/]");

        // Grid: left expands, right fixed at 100 chars (no Expand to avoid full-width forcing)
        var bodyGrid = new Grid();
        bodyGrid.AddColumn(new GridColumn());               // expands
        bodyGrid.AddColumn(new GridColumn { Width = 100 }); // fixed
        bodyGrid.AddRow(new Rows(leftContent), rightContent);
        renderables.Add(bodyGrid);

        // === EVENT LOG (full width, below everything) ===
        var events = _provider.GetEvents(23);
        renderables.Add(new Text("\n"));
        renderables.Add(BuildLogContent(events));

        // === FOOTER ===
        renderables.Add(new Text("\n"));
        renderables.Add(new Markup("[gray]Q:Quit  L:Toggle Log  J:Toggle JSON  E:Export CSV  H:Toggle Config[/]"));

        return renderables;
    }

    private IRenderable CreateTempBar(uint temp, DashboardConfig config)
    {
        var barColor = temp <= config.TargetTemp ? Color.Green
            : temp < config.TriggerTemp ? Color.Yellow
            : Color.Red;

        var barWidth = 40;
        var fillCount = (int)Math.Round((float)temp / config.EmergencyTemp * barWidth);
        fillCount = Math.Clamp(fillCount, 0, barWidth);
        var bar = new string('█', fillCount) + new string(' ', barWidth - fillCount);
        return new Markup($"[{barColor}]{bar}[/]");
    }
    private IRenderable CreatePowerBar(MetricsSnapshot current, DashboardConfig config)
    {
        var range = config.MaxPower - config.MinPower;
        var ratio = range > 0 ? (float)(current.CurrentPowerLimit - config.MinPower) / range : 1;
        ratio = Math.Clamp(ratio, 0, 1);

        var barWidth = 40;
        var fillCount = (int)Math.Round(ratio * barWidth);
        fillCount = Math.Clamp(fillCount, 0, barWidth);
        var bar = new string('█', fillCount) + new string(' ', barWidth - fillCount);
        return new Markup($"[yellow]{bar}[/]");
    }

    private IRenderable BuildHistoryChart(IReadOnlyList<MetricsSnapshot> history)
    {
        var result = new List<IRenderable>();

        // Temperature history
        result.Add(new Markup("[bold]Temperature History[/]"));
        result.Add(BuildAsciiChart(
            history.Select(h => (float)h.Temperature).ToList(),
            "C",
            Color.Cyan,
            minPadding: 2,
            minRange: 5,
            chartHeight: 6,
            chartWidth: 80));

        // Power history
        result.Add(new Markup("[bold]Power History[/]"));
        result.Add(BuildAsciiChart(
            history.Select(h => (float)h.CurrentPowerLimit).ToList(),
            "W",
            Color.Yellow,
            minPadding: 10,
            minRange: 20,
            chartHeight: 6,
            chartWidth: 80));

        return new Rows(result);
    }

    private IRenderable BuildAsciiChart(
        List<float> values,
        string unit,
        Color barColor,
        float minPadding,
        float minRange,
        int chartHeight,
        int chartWidth)
    {
        var sliced = values.TakeLast(chartWidth).ToList();
        var minVal = sliced.Min() - minPadding;
        var maxVal = sliced.Max() + minPadding;
        var range = maxVal - minVal;
        if (range < minRange) range = minRange;

        var segments = new List<Segment>();

        // Build each row
        for (int row = chartHeight - 1; row >= 0; row--)
        {
            var threshold = minVal + (range * (row + 1) / chartHeight);
            segments.Add(new Segment($" {threshold:F0}{unit} ", new Style(Color.Gray)));
            segments.Add(new Segment("│", new Style(Color.Gray)));

            foreach (var v in sliced)
            {
                var height = (v - minVal) / range * chartHeight;
                if (height > row)
                    segments.Add(new Segment("█", new Style(barColor)));
                else
                    segments.Add(new Segment(" ", Style.Plain));
            }
            segments.Add(Segment.LineBreak);
        }

        // Axis line
        segments.Add(new Segment("     " + new string('─', sliced.Count + 1) + " newer", new Style(Color.Gray)));
        segments.Add(Segment.LineBreak);

        return new SegmentString(segments, sliced.Count + 10);
    }

    /// <summary>Toggle event log visibility.</summary>
    public void ToggleLog()
    {
        Interlocked.Exchange(ref _showLogFlag, Interlocked.Read(ref _showLogFlag) != 0 ? 0 : 1);
    }

    /// <summary>Toggle config display.</summary>
    public void ToggleConfig()
    {
        Interlocked.Exchange(ref _showConfigFlag, Interlocked.Read(ref _showConfigFlag) != 0 ? 0 : 1);
    }

    /// <summary>Update JSON publishing status (for display).</summary>
    public void SetJsonStatus(bool enabled)
    {
        Interlocked.Exchange(ref _jsonEnabledFlag, enabled ? 1 : 0);
    }

    /// <summary>Builds the event log content as a Rows renderable.</summary>
    private IRenderable BuildLogContent(IReadOnlyList<DashboardEvent> events)
    {
        var items = new List<IRenderable>();
        items.Add(new Rule("[bold]Event Log[/]"));

        if (Interlocked.Read(ref _showLogFlag) == 0)
        {
            items.Add(new Markup("[dim]Log hidden (press L to show)[/]"));
            return new Rows(items);
        }

        if (events.Count == 0)
        {
            items.Add(new Markup("[dim]No events[/]"));
            return new Rows(items);
        }

        foreach (var evt in events)
        {
            var color = evt.EventType switch
            {
                Core.ControllerEventType.Emergency => Color.Red,
                Core.ControllerEventType.Warning => Color.Yellow,
                Core.ControllerEventType.Trigger => Color.Magenta,
                Core.ControllerEventType.Stable => Color.Green,
                _ => Color.Gray
            };
            var msg = Markup.Escape(evt.Message ?? "");
            var timeStr = Markup.Escape(evt.Timestamp.ToString("HH:mm:ss"));
            items.Add(new Markup($"{timeStr} [{color}]{msg}[/]"));
        }

        return new Rows(items);
    }

    /// <summary>Builds history charts stacked in a fixed-width column.</summary>
    private static IRenderable BuildHistoryColumn(IReadOnlyList<MetricsSnapshot> history)
    {
        var items = new List<IRenderable>();

        // Temperature history (wider for right column)
        items.Add(new Markup("[bold]Temperature History[/]"));
        items.Add(BuildNarrowAsciiChart(
            history.Select(h => (float)h.Temperature).ToList(),
            "C", Color.Cyan,
            minPadding: 2, minRange: 5,
            chartHeight: 6, chartWidth: 80));

        items.Add(new Text(""));

        // Power history
        items.Add(new Markup("[bold]Power History[/]"));
        items.Add(BuildNarrowAsciiChart(
            history.Select(h => (float)h.CurrentPowerLimit).ToList(),
            "W", Color.Yellow,
            minPadding: 10, minRange: 20,
            chartHeight: 6, chartWidth: 80));

        return new Rows(items);
    }

    /// <summary>Builds a narrow ASCII bar chart as segments (for the right column).</summary>
    private static IRenderable BuildNarrowAsciiChart(
        List<float> values, string unit, Color barColor,
        float minPadding, float minRange, int chartHeight, int chartWidth)
    {
        var sliced = values.TakeLast(chartWidth).ToList();
        var minVal = sliced.Min() - minPadding;
        var maxVal = sliced.Max() + minPadding;
        var range = maxVal - minVal;
        if (range < minRange) range = minRange;

        var segments = new List<Segment>();

        for (int row = chartHeight - 1; row >= 0; row--)
        {
            var threshold = minVal + (range * (row + 1) / chartHeight);
            segments.Add(new Segment($" {threshold:F0}{unit} ", new Style(Color.Gray)));
            segments.Add(new Segment("│", new Style(Color.Gray)));

            foreach (var v in sliced)
            {
                var height = (v - minVal) / range * chartHeight;
                if (height > row)
                    segments.Add(new Segment("█", new Style(barColor)));
                else
                    segments.Add(new Segment(" ", Style.Plain));
            }
            segments.Add(Segment.LineBreak);
        }

        segments.Add(new Segment("   " + new string('─', sliced.Count) + " →", new Style(Color.Gray)));
        segments.Add(Segment.LineBreak);

        return new SegmentString(segments, chartWidth + 8);
    }

    /// <summary>Builds a 3-line rounded box as segments with the given style on all parts.</summary>
    private static SegmentString BuildColoredBox(string innerText, Style style)
    {
        var top = $"╭{new string('─', innerText.Length)}╮";
        var mid = $"│{innerText}│";
        var bot = $"╰{new string('─', innerText.Length)}╯";

        var segments = new[]
        {
            new Segment(top, style),
            Segment.LineBreak,
            new Segment(mid, style),
            Segment.LineBreak,
            new Segment(bot, style),
            Segment.LineBreak
        };

        return new SegmentString(segments, mid.Length + 2);
    }

    private static string FormatTimespan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.TotalSeconds:F1}s";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _isRunning = false;
        _renderThread?.Join(2000);
        _renderThread = null;
    }
}

/// <summary>
/// Simple IRenderable that renders a pre-built list of segments.
/// </summary>
public class SegmentString : IRenderable
{
    private readonly Segment[] _segments;
    private readonly int _width;

    public SegmentString(IEnumerable<Segment> segments, int width = 0)
    {
        _segments = segments.ToArray();
        _width = width;
    }

    public Measurement Measure(RenderOptions options, int maxAvailableWidth)
    {
        var minWidth = _width > 0 ? _width : 40;
        return new Measurement(minWidth, maxAvailableWidth);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxAvailableWidth)
    {
        return _segments;
    }

    public override string ToString()
    {
        return string.Join("", _segments);
    }
}


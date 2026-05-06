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
/// Optimized: structural widgets are created once and mutated each frame.
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
    private readonly List<ErrorEntry> _collectedErrors = new();
    private const int MaxLogEntries = 23;

    // === Cached immutable Style objects (never change) ===
    private static readonly Style sStyleYellow = new(Color.Yellow);
    private static readonly Style sStyleRed = new(Color.Red);
    private static readonly Style sStyleGreen = new(Color.Green);
    private static readonly Style sStyleCyan = new(Color.Cyan);
    private static readonly Style sStyleGray = new(Color.Gray);
    private static readonly Style sStyleMagenta = new(Color.Magenta);
    private static readonly Style sStyleBlue = new(Color.Blue);
    private static readonly Style sDimGrayStyle = Style.Parse("dim");

    // === Cached spacing/structural renderables ===
    private static readonly Text sNewline = new("\n");
    private static readonly Text sBlankLine = new("");

    // === Footer text (never changes) ===
    private static readonly Markup sFooterMarkup = new("[gray]Q:Quit  L:Log  J:JSON  E:CSV  H:Config  P:PID  T:TestErr[/]");

    // === Pre-created structural widgets ===
    // Note: Panels and Grids cannot be mutated (no Content property, no Rows.Clear),
    // so they are created fresh each frame. Tables CAN be reused via Rows.Clear().

    // Stats table (4 rows max, reusable via Rows.Clear)
    private readonly Table _statsTable = CreateZeroPaddedTable();

    // PID table (4 rows max, reusable via Rows.Clear)
    private readonly Table _pidTable = CreateZeroPaddedTable();

    // Config table (7 rows max, reusable via Rows.Clear)
    private readonly Table _configTable = new Table().Border(TableBorder.Rounded).AddColumn("Parameter").AddColumn("Value");

    // Log rule (reused each frame - Rule is immutable but the same title)
    private readonly Rule _logRule = new("[bold]Event Log[/]");

    // Waiting for data message (reused when history is empty)
    private readonly Markup _waitingForDataMarkup = new("[dim]Waiting for data...[/]");

    private static Table CreateZeroPaddedTable()
        => new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn(new TableColumn("").Padding(0, 0))
            .AddColumn(new TableColumn("").Padding(0, 0));

    // === Reusable lists (avoid allocation) ===
    private readonly List<IRenderable> _renderables = new(32);
    private readonly List<IRenderable> _leftContent = new(16);
    private readonly List<float> _chartValues = new(80);

    public ConsoleDashboard(IDashboardDataProvider provider)
        : this(provider, AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(System.Console.Out),
            Interactive = InteractionSupport.Yes,
        }))
    {
    }

    /// <summary>Creates a dashboard with a specific console instance (for testing).</summary>
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
        StartRenderThread();
    }

    /// <summary>Pause the dashboard display. Stops the Live display so prompts can be used.</summary>
    public void Pause()
    {
        _isRunning = false;
        var thread = _renderThread;
        _renderThread = null;
        thread?.Join(2000);
    }

    /// <summary>Resume the dashboard display after a Pause().</summary>
    public void Resume()
    {
        if (System.Console.IsOutputRedirected) return;
        _isRunning = true;
        StartRenderThread();
    }

    private void StartRenderThread()
    {
        _renderThread = new Thread(() =>
        {
            _console.Live(new Rows())
                .Overflow(VerticalOverflow.Visible)
                .Start(ctx =>
                {
                    while (_isRunning)
                    {
                        try
                        {
                            RenderFrame();
                            ctx.UpdateTarget(new Rows(_renderables));
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

    private void RenderFrame()
    {
        // Drain pending errors from ErrorConsole into persistent list
        var drained = ErrorConsole.DrainPending();
        foreach (var entry in drained)
            _collectedErrors.Add(entry);

        var config = _provider.Config;
        var current = _provider.Current;

        // Clear reusable collections
        _renderables.Clear();
        _leftContent.Clear();

        // === HEADER ===
        _renderables.Add(new Rule($"[bold green]GpuPowerControl[/] - {Markup.Escape(config.GpuName)}"));
        _renderables.Add(new Markup($"[gray]Uptime: {FormatTimespan(current.Uptime)}[/]"));

        // === STATE & POWER ===
        // Panels cannot be mutated (no Content property), create fresh each frame.
        var stateColor = current.IsControlling ? Color.Yellow
            : current.EventType == Core.ControllerEventType.Emergency ? Color.Red
            : current.EventType == Core.ControllerEventType.Stable ? Color.Green
            : Color.Cyan;

        var statePanel = new Panel(new Text(current.IsControlling ? "CONTROLLING" : "IDLE"))
        {
            BorderStyle = new Style(stateColor),
            Border = BoxBorder.Rounded
        };
        var powerPanel = new Panel(new Text($"{current.CurrentPowerLimit}W / {config.MaxPower}W"))
        {
            BorderStyle = sStyleYellow,
            Border = BoxBorder.Rounded
        };

        var statePowerGrid = new Grid();
        statePowerGrid.AddColumn();
        statePowerGrid.AddColumn(new GridColumn { Width = 22 });
        statePowerGrid.AddRow(statePanel, powerPanel);
        _renderables.Add(statePowerGrid);

        // === TEMPERATURE BAR ===
        _renderables.Add(sNewline);
        _renderables.Add(new Markup($"Temp: [bold]{current.Temperature:F1}C[/]  |  Target: {config.TargetTemp}C  |  Trigger: {config.TriggerTemp}C  |  Emergency: {config.EmergencyTemp}C"));
        _renderables.Add(CreateTempBar(current.Temperature, config));

        // === POWER BAR ===
        _renderables.Add(sNewline);
        _renderables.Add(new Markup($"Power: [bold]{current.CurrentPowerLimit}W[/]  |  Range: {config.MinPower}W - {config.MaxPower}W"));
        _renderables.Add(CreatePowerBar(current, config));

        // === MAIN BODY ===
        // Grid.Rows is IReadOnlyList (no Clear), so create fresh each frame.
        BuildLeftContent(current, config);

        var bodyGrid = new Grid();
        bodyGrid.AddColumn(new GridColumn());
        bodyGrid.AddColumn(new GridColumn { Width = 100 });
        bodyGrid.AddRow(new Rows(_leftContent), BuildHistoryColumn(_provider.GetHistory(80)));
        _renderables.Add(bodyGrid);

        // === EVENT LOG ===
        BuildLogContent(_provider.GetEvents(MaxLogEntries));

        // === FOOTER ===
        _renderables.Add(sNewline);
        _renderables.Add(sFooterMarkup);
    }

    private void BuildLeftContent(MetricsSnapshot current, DashboardConfig config)
    {
        _leftContent.Add(sNewline);

        // Stats table - mutate rows
        _statsTable.Rows.Clear();
        var derivColorName = current.Derivative > 0 ? "yellow" : "green";
        var derivStr = current.Derivative.ToString("+0.0;-0.0;0.0");

        _statsTable.AddRow("Derivative:", $"[{derivColorName}]{derivStr} C/s[/]");
        _statsTable.AddRow("PID Cycles:", $"{current.PidCycles}");
        _statsTable.AddRow("Transitions:", $"{current.StateTransitions}");
        _statsTable.AddRow("Polling:", $"{current.PollingIntervalMs}ms");
        _leftContent.Add(_statsTable);

        // PID breakdown
        if (current.IsControlling && (current.PidP != 0 || current.PidI != 0 || current.PidD != 0))
        {
            _leftContent.Add(sNewline);
            _leftContent.Add(new Markup("[bold]PID Breakdown:[/]"));

            _pidTable.Rows.Clear();
            _pidTable.AddRow("P:", $"[magenta]{current.PidP:+0.0;-0.0;0.0}[/]");
            _pidTable.AddRow("I:", $"[blue]{current.PidI:+0.0;-0.0;0.0}[/]");
            _pidTable.AddRow("D:", $"[green]{current.PidD:+0.0;-0.0;0.0}[/]");
            _pidTable.AddRow("Integral:", $"[gray]{current.PidIntegral:+0.0;-0.0;0.0}[/]");
            _leftContent.Add(_pidTable);
        }

        // Config table (toggleable)
        if (Interlocked.Read(ref _showConfigFlag) != 0)
        {
            _leftContent.Add(sNewline);
            _configTable.Rows.Clear();
            _configTable.AddRow("Kp", config.Kp.ToString("F1"));
            _configTable.AddRow("Ki", config.Ki.ToString("F1"));
            _configTable.AddRow("Kd", config.Kd.ToString("F1"));
            _configTable.AddRow("Target", $"{config.TargetTemp}C");
            _configTable.AddRow("Trigger", $"{config.TriggerTemp}C");
            _configTable.AddRow("Emergency", $"{config.EmergencyTemp}C");
            _configTable.AddRow("Power Range", $"{config.MinPower}W - {config.MaxPower}W");
            _leftContent.Add(_configTable);
        }

        // JSON status
        _leftContent.Add(sNewline);
        var jsonLabel = Interlocked.Read(ref _jsonEnabledFlag) != 0 ? "[green]ON[/]" : "[gray]OFF[/]";
        _leftContent.Add(new Markup($"JSON Publishing: {jsonLabel}  |  Read Failures: {current.ReadFailures}"));
    }

    private void BuildRightContent(MetricsSnapshot current, DashboardConfig config)
    {
        // Right content is built inline in RenderFrame via BuildHistoryColumn
    }

    private IRenderable CreateTempBar(double temp, DashboardConfig config)
    {
        var barColorName = temp <= config.TargetTemp ? "green"
            : temp < config.TriggerTemp ? "yellow"
            : "red";

        var barWidth = 40;
        var fillCount = (int)Math.Round((float)temp / config.EmergencyTemp * barWidth);
        fillCount = Math.Clamp(fillCount, 0, barWidth);
        var bar = new string('█', fillCount) + new string(' ', barWidth - fillCount);
        return new Markup($"[{barColorName}]{bar}[/]");
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

    private IRenderable BuildHistoryColumn(IReadOnlyList<MetricsSnapshot> history)
    {
        if (history.Count < 5) return _waitingForDataMarkup;

        // Reuse chart values list
        _chartValues.Clear();
        foreach (var h in history) _chartValues.Add((float)h.Temperature);
        var tempChart = BuildAsciiChart(_chartValues, "C", Color.Cyan, minPadding: 2, minRange: 5, chartHeight: 6, chartWidth: 80, axisLabel: "→");

        _chartValues.Clear();
        foreach (var h in history) _chartValues.Add((float)h.CurrentPowerLimit);
        var powerChart = BuildAsciiChart(_chartValues, "W", Color.Yellow, minPadding: 10, minRange: 20, chartHeight: 6, chartWidth: 80, axisLabel: "→");

        // Return as a Rows renderable (allocated once per frame, contains ~4 items)
        return new Rows(
            new Markup("[bold]Temperature History[/]"),
            tempChart,
            sBlankLine,
            new Markup("[bold]Power History[/]"),
            powerChart);
    }

    /// <summary>
    /// Builds an ASCII bar chart as a SegmentString.
    /// Merged from BuildAsciiChart + BuildNarrowAsciiChart.
    /// </summary>
    private static IRenderable BuildAsciiChart(
        List<float> values, string unit, Color barColor,
        float minPadding, float minRange, int chartHeight, int chartWidth,
        string axisLabel = "→")
    {
        var sliced = values.TakeLast(chartWidth).ToList();
        var minVal = sliced.Min() - minPadding;
        var maxVal = sliced.Max() + minPadding;
        var range = maxVal - minVal;
        if (range < minRange) range = minRange;

        var segments = new List<Segment>();

        for (int row = chartHeight - 1; row >= 0; row--)
        {
            var threshold = minVal + (range * row / (chartHeight - 1));
            segments.Add(new Segment($" {threshold:F0}{unit} ", sStyleGray));
            segments.Add(new Segment("│", sStyleGray));

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

        segments.Add(new Segment("   " + new string('─', sliced.Count) + " " + axisLabel, sStyleGray));
        segments.Add(Segment.LineBreak);

        return new SegmentString(segments, sliced.Count + 10);
    }

    private void BuildLogContent(IReadOnlyList<DashboardEvent> events)
    {
        _renderables.Add(sNewline);
        _renderables.Add(BuildLogRenderable(events));
    }

    /// <summary>Builds the event log content as a Rows renderable.</summary>
    private IRenderable BuildLogRenderable(IReadOnlyList<DashboardEvent> events)
    {
        var items = new List<IRenderable>();
        items.Add(_logRule);

        if (Interlocked.Read(ref _showLogFlag) == 0)
        {
            items.Add(new Markup("[dim]Log hidden (press L to show)[/]"));
            return new Rows(items);
        }

        // Merge errors and controller events into one chronological list
        var allEntries = new List<LogLine>();

        foreach (var err in _collectedErrors)
        {
            var colorName = err.Level switch
            {
                "ERROR" => "red",
                "WARN" => "yellow",
                _ => "gray"
            };
            var icon = err.Level switch { "ERROR" => "\u2715", "WARN" => "\u26A0", _ => "?" };
            allEntries.Add(new LogLine(err.Timestamp, colorName, $"{icon} {err.Level}: {Markup.Escape(err.Message)}"));
        }

        foreach (var evt in events)
        {
            var colorName = evt.EventType switch
            {
                Core.ControllerEventType.Emergency => "red",
                Core.ControllerEventType.Warning => "yellow",
                Core.ControllerEventType.Trigger => "magenta",
                Core.ControllerEventType.Stable => "green",
                _ => "gray"
            };
            allEntries.Add(new LogLine(evt.Timestamp, colorName, Markup.Escape(evt.Message ?? "")));
        }

        if (allEntries.Count == 0)
        {
            items.Add(new Markup("[dim]No events[/]"));
            return new Rows(items);
        }

        // Sort by timestamp ascending (oldest first), then take the most recent MaxLogEntries
        allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        if (allEntries.Count > MaxLogEntries)
            allEntries = allEntries.Skip(allEntries.Count - MaxLogEntries).ToList();

        // Bound the collected errors list to prevent unbounded growth
        while (_collectedErrors.Count > MaxLogEntries)
            _collectedErrors.RemoveAt(0);

        // Render oldest first (top) to newest (bottom)
        foreach (var line in allEntries)
        {
            var timeStr = line.Timestamp.ToString("HH:mm:ss");
            items.Add(new Markup($"{timeStr} [{line.ColorName}]{line.Text}[/]"));
        }

        return new Rows(items);
    }

    /// <summary>Simple record for a unified log line.</summary>
    private record LogLine(DateTime Timestamp, string ColorName, string Text);

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
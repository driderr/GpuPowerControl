using System;
using System.Collections.Generic;
using System.Linq;
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
    private volatile bool _isRunning;
    private volatile bool _showLog = true;
    private volatile bool _showConfig = false;
    private volatile bool _jsonEnabled = false;
    private Thread? _renderThread;

    public ConsoleDashboard(IDashboardDataProvider provider)
    {
        _provider = provider;
    }

    /// <summary>Start the dashboard display.</summary>
    public void Start()
    {
        _isRunning = true;

        _renderThread = new Thread(() =>
        {
            while (_isRunning)
            {
                try
                {
                    Render();
                }
                catch
                {
                    // Ignore render errors
                }

                Thread.Sleep(250);
            }
        })
        {
            IsBackground = true,
            Name = "DashboardRender"
        };
        _renderThread.Start();
    }

    private void Render()
    {
        var config = _provider.Config;
        var current = _provider.Current;

        // Determine state color
        var stateColor = Color.Default;
        if (current.EventType == Core.ControllerEventType.Emergency) stateColor = Color.Red;
        else if (current.EventType == Core.ControllerEventType.Stable) stateColor = Color.Green;
        else if (current.IsControlling) stateColor = Color.Yellow;
        else stateColor = Color.Cyan;

        var renderables = new List<IRenderable>();

        // === HEADER ===
        renderables.Add(new Rule($"[bold green]GpuPowerControl[/] - {config.GpuName}"));
        renderables.Add(new Text($"[gray]Uptime: {FormatTimespan(current.Uptime)}[/]"));

        // === STATE & POWER ===
        var stateText = current.IsControlling ? "[CONTROLLING]" : "[IDLE]";
        var statePanel = new Panel(new Text(stateText))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(stateColor)
        };

        var powerPanel = new Panel(new Text($"{current.CurrentPowerLimit}W / {config.MaxPower}W"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        renderables.Add(new Columns(new IRenderable[] { statePanel, powerPanel }));

        // === TEMPERATURE BAR ===
        renderables.Add(new Text("\n"));
        renderables.Add(new Text($"Temp: [bold]{current.Temperature}C[/]  |  Target: {config.TargetTemp}C  |  Trigger: {config.TriggerTemp}C  |  Emergency: {config.EmergencyTemp}C"));
        renderables.Add(CreateTempBar(current.Temperature, config));

        // === POWER BAR ===
        renderables.Add(new Text("\n"));
        renderables.Add(new Text($"Power: [bold]{current.CurrentPowerLimit}W[/]  |  Range: {config.MinPower}W - {config.MaxPower}W"));
        renderables.Add(CreatePowerBar(current, config));

        // === DERIVATIVE & STATS ===
        renderables.Add(new Text("\n"));
        var derivColor = current.Derivative > 0 ? Color.Yellow : Color.Green;
        var derivStr = current.Derivative.ToString("+0.0;-0.0;0.0");
        renderables.Add(new Text(
            $"Derivative: [{derivColor}]{derivStr} C/s[/]  |  PID Cycles: {current.PidCycles}  |  Transitions: {current.StateTransitions}  |  Polling: {current.PollingIntervalMs}ms"));

        // === PID BREAKDOWN ===
        if (current.IsControlling && (current.PidP != 0 || current.PidI != 0 || current.PidD != 0))
        {
            renderables.Add(new Text("\n"));
            renderables.Add(new Text("[bold]PID Breakdown:[/]"));
            renderables.Add(new Text(
                $"  P: [magenta]{current.PidP:+0.0;-0.0;0.0}[/]  |  I: [blue]{current.PidI:+0.0;-0.0;0.0}[/]  |  D: [green]{current.PidD:+0.0;-0.0;0.0}[/]  |  Integral: [gray]{current.PidIntegral:+0.0;-0.0;0.0}[/]"));
        }

        // === STATIC CONFIG (toggleable) ===
        if (_showConfig)
        {
            renderables.Add(new Text("\n"));
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
            renderables.Add(configTable);
        }

        // === JSON STATUS ===
        renderables.Add(new Text("\n"));
        var jsonLabel = _jsonEnabled ? "[green]ON[/]" : "[gray]OFF[/]";
        renderables.Add(new Text($"JSON Publishing: {jsonLabel}  |  Read Failures: {current.ReadFailures}"));

        // === HISTORY PANELS ===
        var history = _provider.GetHistory(80);
        if (history.Count >= 5)
        {
            renderables.Add(new Text("\n"));
            renderables.Add(BuildHistoryChart(history));
        }

        // === EVENT LOG ===
        if (_showLog)
        {
            var events = _provider.GetEvents(10);
            if (events.Count > 0)
            {
                renderables.Add(new Text("\n"));
                renderables.Add(new Rule("[bold]Event Log[/]"));
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
                    var msg = evt.Message ?? "";
                    var timeStr = evt.Timestamp.ToString("HH:mm:ss");
                    renderables.Add(new Text($"[{timeStr}] [{color}]{msg}[/]"));
                }
            }
        }

        // === FOOTER ===
        renderables.Add(new Text("\n"));
        renderables.Add(new Text("[gray]Q:Quit  L:Toggle Log  J:Toggle JSON  E:Export CSV  H:Toggle Config[/]"));

        // Render all at once
        Console.Write("\x1b[2J\x1b[H");
        AnsiConsole.Write(new Rows(renderables));
    }

    private IRenderable CreateTempBar(uint temp, DashboardConfig config)
    {
        var width = 40;
        var ratio = Math.Clamp((float)temp / config.EmergencyTemp, 0, 1);
        var filled = (int)(ratio * width);

        var color = temp <= config.TargetTemp ? Color.Green
            : temp < config.TriggerTemp ? Color.Yellow
            : Color.Red;

        var segments = new List<Segment>();
        for (int i = 0; i < width; i++)
        {
            segments.Add(new Segment(i < filled ? "█" : "░", new Style(color)));
        }
        segments.Add(Segment.LineBreak);

        return new SegmentString(segments);
    }

    private IRenderable CreatePowerBar(MetricsSnapshot current, DashboardConfig config)
    {
        var width = 40;
        var range = config.MaxPower - config.MinPower;
        var ratio = range > 0 ? (float)(current.CurrentPowerLimit - config.MinPower) / range : 1;
        ratio = Math.Clamp(ratio, 0, 1);
        var filled = (int)(ratio * width);

        var segments = new List<Segment>();
        for (int i = 0; i < width; i++)
        {
            segments.Add(new Segment(i < filled ? "█" : "░", new Style(Color.Yellow)));
        }
        segments.Add(Segment.LineBreak);

        return new SegmentString(segments);
    }

    private IRenderable BuildHistoryChart(IReadOnlyList<MetricsSnapshot> history)
    {
        var temps = history.Select(h => (float)h.Temperature).ToList();
        var chartWidth = Math.Min(temps.Count, 80);
        var slicedTemps = temps.TakeLast(chartWidth).ToList();

        var minTemp = slicedTemps.Min() - 2;
        var maxTemp = slicedTemps.Max() + 2;
        var tempRange = maxTemp - minTemp;
        if (tempRange < 5) tempRange = 5;

        var chartHeight = 6;
        var segments = new List<Segment>();

        // Title
        segments.Add(new Segment("[bold]Temperature History[/]\n", Style.Plain));

        for (int row = chartHeight - 1; row >= 0; row--)
        {
            var threshold = minTemp + (tempRange * (row + 1) / chartHeight);
            segments.Add(new Segment($" {threshold:F0}C ", new Style(Color.Gray)));
            segments.Add(new Segment("│", new Style(Color.Gray)));

            foreach (var t in slicedTemps)
            {
                var height = (t - minTemp) / tempRange * chartHeight;
                if (height > row)
                    segments.Add(new Segment("█", new Style(Color.Cyan)));
                else
                    segments.Add(new Segment(" ", Style.Plain));
            }
            segments.Add(Segment.LineBreak);
        }

        segments.Add(new Segment("     " + new string('─', chartWidth + 1) + " newer", new Style(Color.Gray)));
        segments.Add(Segment.LineBreak);

        // Power history
        var powers = history.Select(h => (float)h.CurrentPowerLimit).ToList();
        var slicedPowers = powers.TakeLast(chartWidth).ToList();
        var minPower = slicedPowers.Min() - 10;
        var maxPower = slicedPowers.Max() + 10;
        var powerRange = maxPower - minPower;
        if (powerRange < 20) powerRange = 20;

        segments.Add(Segment.LineBreak);
        segments.Add(new Segment("[bold]Power History[/]\n", Style.Plain));

        for (int row = chartHeight - 1; row >= 0; row--)
        {
            var threshold = minPower + (powerRange * (row + 1) / chartHeight);
            segments.Add(new Segment($" {threshold:F0}W ", new Style(Color.Gray)));
            segments.Add(new Segment("│", new Style(Color.Gray)));

            foreach (var p in slicedPowers)
            {
                var height = (p - minPower) / powerRange * chartHeight;
                if (height > row)
                    segments.Add(new Segment("█", new Style(Color.Yellow)));
                else
                    segments.Add(new Segment(" ", Style.Plain));
            }
            segments.Add(Segment.LineBreak);
        }

        segments.Add(new Segment("     " + new string('─', chartWidth + 1) + " newer", new Style(Color.Gray)));
        segments.Add(Segment.LineBreak);

        return new SegmentString(segments);
    }

    /// <summary>Toggle event log visibility.</summary>
    public void ToggleLog()
    {
        _showLog = !_showLog;
    }

    /// <summary>Toggle config display.</summary>
    public void ToggleConfig()
    {
        _showConfig = !_showConfig;
    }

    /// <summary>Update JSON publishing status (for display).</summary>
    public void SetJsonStatus(bool enabled)
    {
        _jsonEnabled = enabled;
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

    public SegmentString(IEnumerable<Segment> segments)
    {
        _segments = segments.ToArray();
    }

    public Measurement Measure(RenderOptions options, int maxAvailableWidth)
    {
        return new Measurement(0, maxAvailableWidth);
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

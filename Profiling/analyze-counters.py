#!/usr/bin/env python3
"""Analyze dotnet-counters JSON output and produce a compact summary.

Usage:
    python Profiling/analyze-counters.py                          # Analyze latest file
    python Profiling/analyze-counters.py Profiling/results/counters-20260506-094602.json

Output:
    - Summary table with min/max/avg for each counter
    - Trend detection (increasing, decreasing, stable)
    - Flags potential issues (memory leaks, CPU hogs, etc.)
"""

import json
import glob
import os
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path


def find_latest_json(results_dir="Profiling/results"):
    """Find the most recent counters JSON file."""
    pattern = os.path.join(results_dir, "counters-*.json")
    files = glob.glob(pattern)
    if not files:
        # Check docs directory for results mentioned in profiling.md
        pattern = os.path.join("Profiling", "results", "counters-*.json")
        files = glob.glob(pattern)
    if not files:
        print("[ERROR] No counters JSON files found in Profiling/results/")
        sys.exit(1)
    return max(files, key=os.path.getmtime)


def human_bytes(value):
    """Convert bytes to human-readable format."""
    for unit in ["By", "KB", "MB", "GB"]:
        if abs(value) < 1024.0:
            return f"{value:.1f} {unit}"
        value /= 1024.0
    return f"{value:.1f} TB"


def format_value(counter_name, value):
    """Format a counter value appropriately."""
    # Byte-based counters
    if any(k in counter_name for k in ["memory.working_set", "heap.", "total_allocated",
                                         "committed_size", "fragmentation"]):
        return human_bytes(value)
    # Time in seconds
    if "time" in counter_name or "time (s" in counter_name:
        if value < 0.001:
            return f"{value * 1_000_000:.0f} us"
        elif value < 1.0:
            return f"{value * 1000:.1f} ms"
        else:
            return f"{value:.2f} s"
    # Rates per second
    if "/ 2 sec" in counter_name:
        return f"{value:.2f}"
    return f"{value:.1f}"


def detect_trend(values):
    """Detect trend using simple linear regression on normalized indices."""
    n = len(values)
    if n < 3:
        return "insufficient data"

    # Split into first third and last third
    third = n // 3
    first_avg = sum(values[:third]) / third
    last_avg = sum(values[2*third:]) / third

    # Avoid division by zero
    if first_avg == 0:
        if last_avg == 0:
            return "stable"
        return "increasing"

    change_pct = ((last_avg - first_avg) / abs(first_avg)) * 100

    if abs(change_pct) < 15:
        return "stable"
    elif change_pct > 0:
        return "increasing"
    else:
        return "decreasing"


def analyze_counter(name, values):
    """Analyze a single counter's values."""
    if not values:
        return None

    return {
        "name": name,
        "count": len(values),
        "min": min(values),
        "max": max(values),
        "avg": sum(values) / len(values),
        "last": values[-1],
        "trend": detect_trend(values),
    }


def get_counter_display_name(name):
    """Get a clean display name for a counter."""
    # Remove the parameter part like " ({assembly})" or " (By)"
    clean = name.split(" (")[0] if " (" in name else name
    # Shorten common prefixes
    clean = clean.replace("dotnet.", "")
    return clean


def flag_issues(analyses):
    """Identify potential performance issues."""
    issues = []

    for a in analyses:
        name = a["name"]
        trend = a["trend"]

        # Memory leak indicators
        if "heap" in name and "size" in name and trend == "increasing":
            issues.append(f"  MEMORY LEAK?: {get_counter_display_name(name)} is steadily increasing ({a['trend']})")
        if "working_set" in name and trend == "increasing":
            change = ((a["last"] - a["min"]) / max(a["min"], 1)) * 100
            if change > 50:
                issues.append(f"  MEMORY PRESSURE: Working set grew {change:.0f}% ({a['trend']})")

        # CPU hog indicators
        if "cpu.time" in name and trend == "increasing":
            issues.append(f"  CPU HOG?: CPU time is increasing ({a['trend']})")

        # Thread pool starvation
        if "thread_pool.thread.count" in name and a["max"] > 50:
            issues.append(f"  THREAD STARVATION?: Thread pool peaked at {a['max']:.0f} threads")

        # GC pressure
        if "gc.collections" in name and "gen2" not in name.lower():
            if a["avg"] > 100:  # More than 100 collections per 2 sec = high
                issues.append(f"  HIGH GC RATE: {a['avg']:.0f} collections per interval")

        # Lock contention
        if "lock_contentions" in name and a["max"] > 0:
            issues.append(f"  LOCK CONTENTION: {a['max']:.0f} contentions detected")

    return issues


def main():
    # Determine input file
    if len(sys.argv) > 1:
        input_file = sys.argv[1]
    else:
        input_file = find_latest_json()

    if not os.path.exists(input_file):
        print(f"[ERROR] File not found: {input_file}")
        sys.exit(1)

    print(f"Analyzing: {input_file}")
    print()

    # Load data
    with open(input_file, "r") as f:
        data = json.load(f)

    events = data["Events"]
    start_time = data.get("StartTime", "unknown")
    target = data.get("TargetProcess", "unknown")

    print(f"Process:   {target}")
    print(f"Started:   {start_time}")
    print(f"Events:    {len(events)}")
    print()

    # Group values by counter name, preserving order
    counter_values = defaultdict(list)
    timestamps = defaultdict(list)

    for event in events:
        name = event["name"]
        value = event["value"]
        # Filter out sentinel -1 values (means "not available" for some counters)
        if value < 0 and "thread_pool" in name:
            continue
        counter_values[name].append(value)
        timestamps[name].append(event["timestamp"])

    # Analyze each counter
    analyses = []
    for name in sorted(counter_values.keys()):
        result = analyze_counter(name, counter_values[name])
        if result:
            analyses.append(result)

    # Print summary table
    print("=" * 90)
    print(f"{'Counter':<45} {'Trend':<12} {'Min':<14} {'Max':<14} {'Avg':<14}")
    print("=" * 90)

    for a in analyses:
        display = get_counter_display_name(a["name"])
        trend_marker = {"stable": "=", "increasing": "^", "decreasing": "v",
                        "insufficient data": "?"}.get(a["trend"], "?")
        trend_str = f"{trend_marker} {a['trend']}"

        min_str = format_value(a["name"], a["min"])
        max_str = format_value(a["name"], a["max"])
        avg_str = format_value(a["name"], a["avg"])

        print(f"{display:<45} {trend_str:<12} {min_str:<14} {max_str:<14} {avg_str:<14}")

    print("=" * 90)
    print()

    # Report issues
    issues = flag_issues(analyses)
    if issues:
        print("POTENTIAL ISSUES:")
        for issue in issues:
            print(issue)
        print()
    else:
        print("No obvious performance issues detected.")
        print()

    # Summary stats
    print(f"Sampled {len(analyses)} counters over {analyses[0]['count'] if analyses else 0} intervals each.")


if __name__ == "__main__":
    main()
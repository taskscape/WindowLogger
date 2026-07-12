using System;
using System.Linq;
using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;

namespace WindowLoggerConfigGui;

public enum DashboardPeriod
{
    Day,
    Week,
    Month
}

public sealed class DashboardBarItem
{
    public required DashboardBreakdown Data { get; init; }
    public required double BarWidth { get; init; }
    public string Name => Data.Name;
    public string Summary => $"{FormatDuration(Data.TotalMinutes)} (A {FormatDuration(Data.ActiveMinutes)} / I {FormatDuration(Data.IdleMinutes)})";
    public string Comparison => Data.ChangePercent is null
        ? $"Previous {FormatDuration(Data.PreviousMinutes)} • new"
        : $"Previous {FormatDuration(Data.PreviousMinutes)} • {Data.ChangePercent:+0.#;-0.#;0}%";

    public static string FormatDuration(double minutes)
    {
        int rounded = Math.Max(0, (int)Math.Round(minutes));
        return rounded >= 60 ? $"{rounded / 60}h {rounded % 60}m" : $"{rounded}m";
    }
}

public sealed class RuleTraceViewModel
{
    public required RuleEvaluationTrace Trace { get; init; }
    public string Heading => $"{Trace.RuleType} #{Trace.RuleIndex + 1}: {Trace.RuleName}";
    public string Patterns => Trace.Patterns.Count == 0
        ? "No patterns configured"
        : string.Join("  •  ", Trace.Patterns.Select(pattern =>
            $"{(pattern.Matched ? "✓" : "✗")} {pattern.Kind} ‘{pattern.Pattern}’"));
    public string Outcome => Trace.Outcome;
}

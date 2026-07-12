using System;
using WindowLogger.Shared.Activity;

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

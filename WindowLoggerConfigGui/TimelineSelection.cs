using System;

namespace WindowLoggerConfigGui;

public sealed record TimelineSelection(DateTime Start, DateTime End)
{
    public static TimelineSelection? FromPositions(
        double anchor, double position, double width, DateTime rangeStart, DateTime rangeEnd)
    {
        if (width <= 0 || rangeEnd <= rangeStart)
        {
            return null;
        }
        double left = Math.Clamp(Math.Min(anchor, position), 0, width);
        double right = Math.Clamp(Math.Max(anchor, position), 0, width);
        if (right - left < 3)
        {
            return null;
        }
        long ticks = (rangeEnd - rangeStart).Ticks;
        return new TimelineSelection(
            rangeStart.AddTicks((long)Math.Round(ticks * (left / width))),
            rangeStart.AddTicks((long)Math.Round(ticks * (right / width))));
    }
}

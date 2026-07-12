using WindowLogger.Shared.Activity;

namespace WindowLogger.Tests;

public sealed class ActivityDatabaseTests
{
    [Test]
    public void Records_are_ordered_and_filtered_by_indexed_timestamp_range()
    {
        string path = CreateDatabasePath();
        try
        {
            DateTime day = new(2026, 7, 10);
            using var database = new ActivityDatabase(path);
            database.Append(ActivityRecord.Create(day.AddHours(11), "Later [two.exe]", "Active"));
            database.Append(ActivityRecord.Create(day.AddHours(9), string.Empty, "SessionStart"));
            database.Append(ActivityRecord.Create(day.AddHours(10), "In range [one.exe]", "Active"));

            List<ActivityRecord> records = database.Read(day.AddHours(9.5), day.AddHours(10.5)).ToList();

            Assert.That(records.Select(record => record.WindowTitle), Is.EqualTo(new[] { "In range [one.exe]" }));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Test]
    public void Reader_uses_a_stable_snapshot_while_another_connection_writes()
    {
        string path = CreateDatabasePath();
        try
        {
            DateTime start = new(2026, 7, 10, 9, 0, 0);
            using var writer = new ActivityDatabase(path);
            writer.Append(ActivityRecord.Create(start, string.Empty, "SessionStart"));
            writer.Append(ActivityRecord.Create(start.AddMinutes(1), "Initial [one.exe]", "Active"));

            using var reader = new ActivityDatabase(path, readOnly: true);
            using IEnumerator<ActivityRecord> snapshot = reader.Read().GetEnumerator();
            Assert.That(snapshot.MoveNext(), Is.True);

            writer.Append(ActivityRecord.Create(start.AddMinutes(2), "Concurrent [two.exe]", "Active"));
            var remainingSnapshot = new List<ActivityRecord>();
            while (snapshot.MoveNext())
            {
                remainingSnapshot.Add(snapshot.Current);
            }

            Assert.Multiple(() =>
            {
                Assert.That(remainingSnapshot.Any(record => record.WindowTitle.StartsWith("Concurrent")), Is.False);
                Assert.That(reader.Read().Any(record => record.WindowTitle.StartsWith("Concurrent")), Is.True);
            });
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Test]
    public void Failed_bulk_import_is_rolled_back_atomically()
    {
        string path = CreateDatabasePath();
        try
        {
            using var database = new ActivityDatabase(path);
            Assert.Throws<InvalidOperationException>(() => database.Import(FailingRecords()));
            Assert.That(database.Count, Is.Zero);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Test]
    public void Empty_replacement_import_does_not_erase_existing_activity()
    {
        string path = CreateDatabasePath();
        try
        {
            using var database = new ActivityDatabase(path);
            database.Append(ActivityRecord.Create(new DateTime(2026, 7, 10, 9, 0, 0), string.Empty, "SessionStart"));

            Assert.Throws<InvalidDataException>(() => database.Import([], replace: true));
            Assert.That(database.Count, Is.EqualTo(1));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Test]
    public void Csv_export_and_import_preserve_quoted_titles_and_statuses()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"WindowLogger-{Guid.NewGuid():N}.csv");
        try
        {
            ActivityRecord[] source =
            [
                ActivityRecord.Create(new DateTime(2026, 7, 10, 9, 0, 0), string.Empty, "SessionStart"),
                ActivityRecord.Create(new DateTime(2026, 7, 10, 9, 1, 0), "Document, Part 1 [test.exe]", "Active"),
                ActivityRecord.Create(new DateTime(2026, 7, 10, 9, 2, 0), string.Empty, "SessionEnd")
            ];

            ActivityCsv.Write(csvPath, source);
            List<ActivityRecord> restored = ActivityCsv.Read(csvPath).ToList();

            Assert.That(restored, Is.EqualTo(source));
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static IEnumerable<ActivityRecord> FailingRecords()
    {
        yield return ActivityRecord.Create(new DateTime(2026, 7, 10, 9, 0, 0), string.Empty, "SessionStart");
        throw new InvalidOperationException("Simulated import failure.");
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"WindowLogger-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (string candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            File.Delete(candidate);
        }
    }
}

using System.IO;

namespace D3Offline.CombatMeter;

// Uses the server's monotonic clock, never arrival timing. UI retention does not truncate totals.
public sealed class MeterHistory
{
    private readonly Dictionary<string, Encounter> _encounters = new();
    public string Session { get; private set; } = "";
    public long LastSequence { get; private set; }
    public long LostHits { get; private set; }
    public double Now { get; private set; }
    public List<CombatHit> Hits { get; } = new();
    public List<CombatPlayer> Players { get; private set; } = new();

    public void Accept(MeterFrame frame)
    {
        if (frame.Version != MeterProtocol.Version) throw new InvalidDataException("Combat meter protocol version differs from the server.");
        if (Session != frame.Session)
        {
            Reset();
            Session = frame.Session;
            LastSequence = 0;
        }
        Now = frame.Now;
        Players = frame.Players;
        LostHits += frame.LostHits;
        foreach (var hit in frame.Hits.Where(h => h.Sequence > LastSequence))
        {
            Hits.Add(hit);
            var key = hit.PlayerId + ":" + hit.Direction;
            if (!_encounters.TryGetValue(key, out var encounter))
                _encounters.Add(key, encounter = new Encounter { Start = hit.Time });
            encounter.Total += hit.HealthRemoved;
            encounter.Count++;
            encounter.Recent.Enqueue((hit.Time, hit.HealthRemoved));
        }
        LastSequence = frame.LastSequence;
        if (Hits.Count > 1000) Hits.RemoveRange(0, Hits.Count - 1000);
        foreach (var encounter in _encounters.Values)
            while (encounter.Recent.TryPeek(out var hit) && Now - hit.Time >= 30) encounter.Recent.Dequeue();
    }

    public void Reset()
    {
        _encounters.Clear();
        Hits.Clear();
        LostHits = 0;
    }

    public (double Rolling, double Average, double Total, long Count) Stats(string player, string direction)
    {
        if (!_encounters.TryGetValue(player + ":" + direction, out var encounter)) return default;
        // Include idle time. A hit at t=0 is a finite impulse, not infinite instantaneous DPS.
        var duration = Math.Max(.25, Now - encounter.Start);
        return (encounter.Recent.Where(h => Now - h.Time < 3).Sum(h => h.Damage) / Math.Min(3, duration),
            encounter.Total / duration, encounter.Total, encounter.Count);
    }

    public double[] Chart(string player, string direction)
    {
        var bins = new double[60];
        if (!_encounters.TryGetValue(player + ":" + direction, out var encounter)) return bins;
        foreach (var hit in encounter.Recent)
        {
            var index = 59 - (int)((Now - hit.Time) / .5);
            if (index >= 0 && index < bins.Length) bins[index] += hit.Damage * 2;
        }
        return bins;
    }

    private sealed class Encounter
    {
        public double Start, Total;
        public long Count;
        public Queue<(double Time, double Damage)> Recent { get; } = new();
    }
}

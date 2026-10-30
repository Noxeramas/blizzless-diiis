#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace D3Offline.CombatMeter;

// Local diagnostic protocol, deliberately independent of Diablo's client protocol.
public static class MeterProtocol
{
    public const string PipeName = "D3Offline.CombatMeter.v1";
    public const int Version = 1;
    public const string Additive = "Additive A+B+C";
    public const string Elemental = "Elemental";
    public const string Independent = "Independent";
    public const string Unverified = "Unclassified";

    // Only compare the captured, classified factors. This is not a complete retail prediction:
    // weapon totals, pet rules, snapshots and unimplemented powers require separate evidence.
    public static double ReferenceProduct(IEnumerable<DamageFactor> factors) => factors
        .Where(f => f.ReferenceBucket != Unverified && f.Kind == "Multiplier")
        .GroupBy(f => f.ReferenceBucket == Independent ? "Independent:" + f.Name : f.ReferenceBucket)
        .Aggregate(1d, (total, group) => total * (group.Key.StartsWith("Independent:", StringComparison.Ordinal)
            ? group.Aggregate(1d, (value, f) => value * f.Multiplier)
            : 1d + group.Sum(f => f.Multiplier - 1d)));
}

public sealed class MeterFrame
{
    public int Version { get; set; } = MeterProtocol.Version;
    public string Session { get; set; } = "";
    public double Now { get; set; }
    public long LastSequence { get; set; }
    public long LostHits { get; set; }
    public List<CombatHit> Hits { get; set; } = new();
    public List<CombatPlayer> Players { get; set; } = new();
}

public sealed class CombatHit
{
    public long Sequence { get; set; }
    public double Time { get; set; }
    public string PlayerId { get; set; } = "";
    public string Player { get; set; } = "";
    public string Direction { get; set; } = "Outgoing";
    public int PowerSno { get; set; }
    public string Skill { get; set; } = "";
    public string Target { get; set; } = "";
    public string Element { get; set; } = "";
    public bool Critical { get; set; }
    public bool ItemProc { get; set; }
    public string Outcome { get; set; } = "Hit";
    public double BaseDamage { get; set; }
    public double FinalDamage { get; set; }
    public double HealthRemoved { get; set; }
    public double Overkill { get; set; }
    public List<DamageFactor> Factors { get; set; } = new();
}

public sealed class DamageFactor
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Multiplier";
    public string ReferenceBucket { get; set; } = MeterProtocol.Unverified;
    public string Evidence { get; set; } = "Live rule unverified";
    public string Source { get; set; } = "";
    public double Before { get; set; }
    public double After { get; set; }
    public double Multiplier { get; set; } = 1;
}

public sealed class CombatPlayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Time { get; set; }
    public bool Paused { get; set; }
    public double Shield { get; set; }
    public List<CombatBuff> Buffs { get; set; } = new();
}

public sealed class CombatBuff
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "Observed";
    public bool IsStacking { get; set; }
    public int Stacks { get; set; }
    public double? SecondsLeft { get; set; }
    public string Detail { get; set; } = "";
}

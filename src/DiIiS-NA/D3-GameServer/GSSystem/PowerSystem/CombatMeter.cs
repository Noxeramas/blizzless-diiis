using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using D3Offline.CombatMeter;
using DiIiS_NA.Core.Logging;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.ActorSystem.Implementations;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations;
using DiIiS_NA.GameServer.MessageSystem;

namespace DiIiS_NA.GameServer.GSSystem.PowerSystem;

/// <summary>Read-only, opt-in tracing. No serialization or pipe IO runs on the game thread.</summary>
public static class CombatMeter
{
    private static readonly Logger Logger = LogManager.CreateLogger();
    private static readonly object Gate = new();
    private static readonly Queue<CombatHit> Hits = new();
    private static readonly Dictionary<string, CombatPlayer> Players = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string Session = Guid.NewGuid().ToString("N");
    private static long _sequence;
    private static int _started, _connected;
    public static bool Enabled => Volatile.Read(ref _connected) != 0;
    public static double Now => Clock.Elapsed.TotalSeconds;

    public static void Start(string pipeName = MeterProtocol.PipeName)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) _ = Task.Run(() => Serve(pipeName));
    }

    private static async Task Serve(string pipeName)
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                long cursor;
                lock (Gate) { Hits.Clear(); Players.Clear(); cursor = _sequence; }
                Volatile.Write(ref _connected, 1);
                while (pipe.IsConnected)
                {
                    // One bounded command per snapshot prevents an unresponsive viewer from retaining tracing.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var command = new char[1];
                    if (await reader.ReadAsync(command.AsMemory(), timeout.Token) != 1 || command[0] != '?') break;
                    MeterFrame frame;
                    lock (Gate)
                    {
                        frame = new MeterFrame { Session = Session, Now = Now, LastSequence = _sequence,
                            LostHits = Hits.Count == 0 ? 0 : Math.Max(0, Hits.Peek().Sequence - cursor - 1),
                            Hits = Hits.Where(h => h.Sequence > cursor).ToList(),
                            Players = Players.Values.Where(p => Now - p.Time < 3).ToList() };
                    }
                    cursor = frame.LastSequence;
                    await writer.WriteLineAsync(JsonSerializer.Serialize(frame).AsMemory(), timeout.Token);
                }
            }
            catch (OperationCanceledException) { /* The viewer stopped requesting frames. */ }
            catch (IOException ex)
            {
                Logger.Debug("Combat meter disconnected: {0}", ex.Message);
                await Task.Delay(250);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Warn("Combat meter pipe unavailable: {0}", ex.Message);
                await Task.Delay(1000);
            }
            finally { Volatile.Write(ref _connected, 0); }
        }
    }

    public static string PlayerId(Player player) => $"{player.World.Game.GameId}:{player.GlobalID}";

    public static CombatHit Begin(Actor attacker, Actor target, int power, bool critical, bool itemProc)
    {
        if (!Enabled) return null;
        var owner = attacker as Player ?? (attacker as Minion)?.Master as Player;
        var player = owner ?? target as Player;
        if (player == null || target is DesctructibleLootContainer) return null;
        return new CombatHit { PlayerId = PlayerId(player), Player = player.Toon?.Name ?? "Player",
            Direction = owner == null ? "Incoming" : "Outgoing", PowerSno = power,
            Skill = PowerName(power), Target = $"{target.SNO} · {target.GlobalID}",
            Critical = critical, ItemProc = itemProc };
    }

    public static string PowerName(int power) => power switch
    {
        77113 => "Energy Twister", 134456 => "Arcane Torrent", 91549 => "Disintegrate",
        93395 => "Ray of Frost", 76108 => "Magic Weapon", 74499 => "Storm Armor",
        99120 => "Familiar", _ => $"Power {power}"
    };

    public static string BuffName(Buff buff)
    {
        if (buff is GalvanizingBuff) return "Galvanizing Ward recharge controller";
        if (buff is GalvShieldBuff) return "Galvanizing Ward shield";
        if (buff is StormArmor.StormArmorBuff) return "Storm Armor";
        if (buff is StormArmor.GoldenBuff) return "Power of the Storm";
        if (buff is MagicWeapon.DeflectionBuff) return "Magic Weapon · Deflection shield";
        if (buff is AquilaBuff) return "Aquila Cuirass";
        if (buff is ConventionBuff) return "Convention of Elements";
        if (buff is SquirtsBuff) return "Squirt's Necklace";
        if (buff is StoneGauntletsBuff) return "Stone Gauntlets";
        var type = buff.GetType();
        var name = type.IsNested ? type.DeclaringType.Name + " · " + type.Name : type.Name;
        return Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1");
    }

    public static void Record(CombatHit hit, float damage, float oldHp, float newHp, string outcome = "Hit")
    {
        if (hit == null) return;
        hit.Time = Now;
        hit.FinalDamage = damage;
        hit.HealthRemoved = Math.Max(0, oldHp - newHp);
        hit.Overkill = outcome == "Hit" ? Math.Max(0, damage - hit.HealthRemoved) : 0;
        hit.Outcome = outcome;
        lock (Gate)
        {
            hit.Sequence = ++_sequence;
            Hits.Enqueue(hit);
            while (Hits.Count > 2048) Hits.Dequeue();
        }
    }

    // Call only from the owning game update thread, including paused games.
    public static void CapturePlayers(IEnumerable<Player> players, bool paused)
    {
        if (!Enabled) return;
        foreach (var player in players)
        {
            if (player.World == null) continue;
            var state = new CombatPlayer { Id = PlayerId(player), Name = player.Toon?.Name ?? "Player",
                Time = Now, Paused = paused, Shield = player.Attributes[GameAttributes.Damage_Shield_Amount] };
            if (player.LegacyOfDreamsRank.HasValue)
                state.Buffs.Add(new CombatBuff { Name = "Legacy of Dreams",
                    State = player.LegacyOfDreamsSetBonusActive ? "Inactive" : "Active",
                    Detail = LegacyOfDreamsSource(player) });
            var buffs = player.World.BuffManager.GetAllBuffs(player).Keys
                .Where(b => !b.Removed && (b is not TimedBuff { Timeout: { } timer } || timer.TimeoutTick > player.World.Game.TickCounter)).ToList();
            var ward = buffs.OfType<GalvanizingBuff>().FirstOrDefault();
            var shield = buffs.OfType<GalvShieldBuff>().FirstOrDefault();
            if (ward != null || shield != null)
            {
                var selected = player.SkillSet.HasPassive(208541);
                state.Buffs.Add(new CombatBuff { Name = "Galvanizing Ward",
                    State = !selected ? "Unexpected effect" : shield == null ? "Recharging" : shield.RemainingAbsorption > 0 ? "Shield active" : "Depleted",
                    SecondsLeft = SecondsLeft(shield, player),
                    Detail = (shield != null ? $"Shield remaining: {shield.RemainingAbsorption:N0}. " : "No shield currently. ") +
                        (selected ? "Selected passive; recharge controller is not a second buff." : "Server effect exists although this passive is not selected.") });
            }
            var armor = buffs.OfType<StormArmor.StormArmorBuff>().FirstOrDefault();
            var stormRune = buffs.OfType<StormArmor.GoldenBuff>().FirstOrDefault();
            if (armor != null || stormRune != null)
            {
                state.Buffs.Add(new CombatBuff { Name = stormRune == null ? "Storm Armor" : "Storm Armor · Power of the Storm",
                    State = armor == null ? "Unexpected effect" : "Active",
                    SecondsLeft = SecondsLeft(armor ?? (Buff)stormRune, player),
                    Detail = (armor == null ? "Rune effect remains without Storm Armor. " : "Lightning attack effect. ") +
                        (stormRune == null ? "" : "Power of the Storm reduces resource costs; it is not an extra damage multiplier.") });
            }
            foreach (var buff in buffs)
            {
                if (buff is GalvanizingBuff or GalvShieldBuff or StormArmor.StormArmorBuff or StormArmor.GoldenBuff) continue;
                var active = buff switch
                {
                    TwistedSwordBuff sword => sword.DamageBonus > 0,
                    EtchedSigilBuff sigil => sigil.DamageBonus > 0,
                    MantleOfChannelingBuff mantle => mantle.DamageBonus > 0,
                    FlameWardBuff wardBuff => wardBuff.DamageReduction > 0,
                    AquilaBuff aquila => aquila.DamageReduction > 0,
                    ConventionBuff convention => convention.Bonus(convention.CurrentElement) > 0,
                    SquirtsBuff squirts => squirts.DamageBonus > 0,
                    StoneGauntletsBuff stone => stone.StackCount > 0,
                    _ => (bool?)null
                };
                state.Buffs.Add(new CombatBuff { Name = BuffName(buff),
                    State = active == true ? "Active" : active == false ? "Inactive" : "Observed",
                    IsStacking = buff is PowerBuff { IsCountingStacks: true },
                    Stacks = buff is PowerBuff powerBuff ? powerBuff.StackCount : 1,
                    SecondsLeft = SecondsLeft(buff, player),
                    Detail = buff switch
                    {
                        TwistedSwordBuff sword => $"Twister ×{1 + sword.DamageBonus:0.###}",
                        EtchedSigilBuff sigil => $"Twister ×{1 + sigil.DamageBonus:0.###}",
                        MantleOfChannelingBuff mantle => $"Outgoing ×{1 + mantle.DamageBonus:0.###}; incoming ×{1 - mantle.DamageReduction:0.###}",
                        FlameWardBuff wardBuff => $"Incoming ×{1 - wardBuff.DamageReduction:0.###}",
                        AquilaBuff aquila => $"Incoming ×{1 - aquila.DamageReduction:0.###}; requires primary resource above the item threshold",
                        ConventionBuff convention => $"{(DamageType.HitEffectType)convention.CurrentElement} ×{1 + convention.Bonus(convention.CurrentElement):0.###}",
                        SquirtsBuff squirts => $"Outgoing ×{1 + squirts.DamageBonus:0.###}; incoming ×{1 + squirts.DamageTakenBonus:0.###}; shields preserve stacks",
                        StoneGauntletsBuff stone => $"+{stone.StackCount * 50}% armor; Ice Climbers suppresses movement and attack penalties",
                        _ => "Server effect present; activity and damage contribution not classified."
                    } });
            }
            lock (Gate)
            {
                foreach (var stale in Players.Where(p => Now - p.Value.Time >= 3).Select(p => p.Key).ToArray()) Players.Remove(stale);
                Players[state.Id] = state;
            }
        }
    }

    private static double? SecondsLeft(Buff buff, Player player) =>
        buff is TimedBuff { Timeout: { } timer } ? Math.Max(0, (timer.TimeoutTick - player.World.Game.TickCounter) / 60d) : null;

    public static string ItemSource(Actor actor, int powerSno, string condition)
    {
        if (!Enabled) return "";
        if (actor is not Player player) return condition;
        var roll = player.GetLegendaryPowerValue(powerSno);
        var source = player.CubedPowerValues.ContainsKey(powerSno) ? "Cube (equipped duplicate excluded)" : "Equipment";
        return roll > 0 ? $"{source} · roll {roll:P0} · {condition}" : "Power not equipped or cubed";
    }

    public static string LegacyOfDreamsSource(Player player)
    {
        if (!Enabled || player.LegacyOfDreamsRank is not { } rank) return "";
        var perItem = 3.75f * (rank + 1);
        var counts = $"{player.LegacyOfDreamsLegendaryItems - player.LegacyOfDreamsAncientItems} non-ancient + {player.LegacyOfDreamsAncientItems} ancient legendary/set items";
        var ancient = rank >= 25 ? $"Ancients: +{2 * perItem:0.##}% damage and 4% reduction each (doubled)." : "Ancient doubling unlocks at rank 25.";
        var condition = player.LegacyOfDreamsSetBonusActive ? "Disabled by an active set bonus." : "No active set bonus.";
        return $"Rank {rank} · +{perItem:0.##}% damage and 2% reduction per legendary. {ancient} " +
            $"Equipped: {counts}. {condition} Total: +{(player.LegacyOfDreamsDamage - 1) * 100:0.##}% damage (×{player.LegacyOfDreamsDamage:0.####}); " +
            $"{(1 - player.LegacyOfDreamsDamageTaken) * 100:0.##}% damage reduction (incoming ×{player.LegacyOfDreamsDamageTaken:0.####}).";
    }
}

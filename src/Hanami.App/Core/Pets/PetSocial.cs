namespace Hanami.Core.Pets;

/// <summary>
/// Pets greeting each other. Pure; the manager calls <see cref="Update"/> every <see cref="EveryTicks"/> ticks with every
/// pet's brain and <see cref="TargetX"/> for each follower every tick. Brains know nothing about each other: this class
/// holds the pair cooldowns and who follows whom.
/// <para>
/// Two pets on the same surface, both free (<see cref="PetBrain.IsFree"/>: standing, walking, running or sitting, neither on
/// Stay nor Follow me), within 90 px (at pixel scale 1) of each other and past their pair cooldown (60 s; calm 120 s, lively 30 s)
/// <see cref="PetBrain.Meet"/> each other: face each other with hearts for about 2 s. Then 40 % both sit for 5 – 15 s (both need
/// the sit frames), 30 % one follows the other for up to 15 s, 30 % they walk off in opposite directions.
/// </para>
/// </summary>
public sealed class PetSocial
{
    public const int EveryTicks = 10;

    private readonly Dictionary<(string, string), long> _cooldownUntil = new();
    private readonly Dictionary<string, string> _follows = new();   // follower → followed

    private static (string, string) Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    public static int CooldownTicks(string activity) =>
        ShimejiFrames.TickRate * (activity == "calm" ? 120 : activity == "lively" ? 30 : 60);

    /// <summary>Looks for pets to bring together at tick <paramref name="tick"/>. At most one pair per call.</summary>
    public void Update(IReadOnlyList<(string Id, PetBrain Brain)> pets, long tick, string activity, Random rng)
    {
        // Followers whose pet stopped following (time up, interrupted) are forgotten.
        foreach (var f in _follows.Keys.ToList())
            if (pets.FirstOrDefault(p => p.Id == f).Brain is not { State: PetState.FollowPet or PetState.Meet }) _follows.Remove(f);

        for (var i = 0; i < pets.Count; i++)
            for (var j = i + 1; j < pets.Count; j++)
            {
                var (ida, a) = pets[i];
                var (idb, b) = pets[j];
                if (!a.IsFree || !b.IsFree || a.Surface is not { } sa || b.Surface is not { } sb) continue;
                if (sa.Owner != sb.Owner || sa.At != sb.At) continue;
                if (Math.Abs(a.X - b.X) > 90 * Math.Max(a.PixelScale, b.PixelScale)) continue;
                var key = Key(ida, idb);
                if (_cooldownUntil.TryGetValue(key, out var until) && tick < until) continue;

                _cooldownUntil[key] = tick + CooldownTicks(activity);
                Introduce(ida, a, idb, b, rng);
                return;
            }
    }

    private void Introduce(string ida, PetBrain a, string idb, PetBrain b, Random rng)
    {
        var roll = rng.NextDouble();
        var ax = a.X;
        var bx = b.X;
        if (roll < 0.4 && a.Caps.Sit && b.Caps.Sit)
        {
            var ticks = ShimejiFrames.TickRate * (5 + rng.Next(11));
            a.Meet(bx, new MeetPlan(MeetKind.Sit, ticks));
            b.Meet(ax, new MeetPlan(MeetKind.Sit, ticks));
        }
        else if (roll < 0.7)
        {
            var aFollows = rng.Next(2) == 0;
            var (follower, followerId, leaderId) = aFollows ? (a, ida, idb) : (b, idb, ida);
            var ticks = ShimejiFrames.TickRate * 15;
            follower.Meet(aFollows ? bx : ax, new MeetPlan(MeetKind.Follow, ticks));
            (aFollows ? b : a).Meet(aFollows ? ax : bx, new MeetPlan(MeetKind.None, 0));
            _follows[followerId] = leaderId;
        }
        else
        {
            a.Meet(bx, new MeetPlan(MeetKind.Part, 0));
            b.Meet(ax, new MeetPlan(MeetKind.Part, 0));
        }
    }

    /// <summary>
    /// Where the pet that <paramref name="followerId"/> follows stands, or null when it is not following anyone or the other has left
    /// the follower's surface. Feed it to <see cref="PetBrain.PetTargetX"/> every tick.
    /// </summary>
    public double? TargetX(string followerId, IReadOnlyList<(string Id, PetBrain Brain)> pets)
    {
        if (!_follows.TryGetValue(followerId, out var leaderId)) return null;
        var follower = pets.FirstOrDefault(p => p.Id == followerId).Brain;
        var leader = pets.FirstOrDefault(p => p.Id == leaderId).Brain;
        if (follower?.Surface is not { } fs || leader?.Surface is not { } ls) return null;
        if (fs.Owner != ls.Owner || fs.At != ls.At || ls.Kind != SurfaceKind.Floor) return null;
        return leader.X;
    }

    public void Forget(string petId)
    {
        _follows.Remove(petId);
        foreach (var f in _follows.Where(kv => kv.Value == petId).Select(kv => kv.Key).ToList()) _follows.Remove(f);
        foreach (var k in _cooldownUntil.Keys.Where(k => k.Item1 == petId || k.Item2 == petId).ToList()) _cooldownUntil.Remove(k);
    }
}

namespace Hanami.Core.Pets;

/// <summary>One frame of an animation: which shime image, for how many ticks, where the pet's "foot" is on it and how it moves.</summary>
/// <param name="Frame">N of shime&lt;N&gt;.png.</param>
/// <param name="Ticks">Duration in 1/25 s ticks.</param>
/// <param name="AnchorX">Where the pet stands on the image, in image pixels (authored for 100% size).</param>
/// <param name="AnchorY">As above; 128 = the bottom edge of a 128 px frame, 48 = a ceiling grip.</param>
/// <param name="Vx">Movement per tick in pixels, for a pet facing LEFT (Shimeji frames face left; mirror for right).</param>
/// <param name="Vy">Movement per tick in pixels (negative = up).</param>
/// <param name="Mirror">The image faces the opposite way to the usual left, so it is mirrored when the pet faces left instead of right.</param>
public readonly record struct Pose(int Frame, int Ticks, int AnchorX, int AnchorY, int Vx, int Vy, bool Mirror = false);

/// <summary>
/// What the numbered frames of a Shimeji image set (shime1.png … shime46.png) mean, and how long and how far
/// each animation moves. Taken from Shimeji-ee's default conf/actions.xml, as a table: the packs' own
/// actions.xml / behaviors.xml are not run. Source: github.com/chaoskagami/shimeji-ee, conf/actions.xml at
/// commit 46bf9e056d12ffd72e8c6e39b3399184b910769f (the same file as gil/shimeji-ee). Kept to the animations
/// Hanami's own behaviour logic uses.
/// </summary>
public static class ShimejiFrames
{
    public const int TickRate = 25;
    public const int NumFrames = 46;

    /// <summary>Embedded Fall action: pixels per tick² of gravity, and the air resistance per tick.</summary>
    public const double Gravity = 2, ResistanceX = 0.05, ResistanceY = 0.1;

    private static Pose[] Seq(int ax, int ay, params (int frame, int ticks, int vx, int vy)[] p) =>
        p.Select(x => new Pose(x.frame, x.ticks, ax, ay, x.vx, x.vy)).ToArray();

    // ---- on a floor ----------------------------------------------------------------------------
    public static readonly Pose[] Stand = Seq(64, 128, (1, 250, 0, 0));
    public static readonly Pose[] Walk = Seq(64, 128, (1, 6, -2, 0), (2, 6, -2, 0), (1, 6, -2, 0), (3, 6, -2, 0));
    public static readonly Pose[] Run = Seq(64, 128, (1, 2, -4, 0), (2, 2, -4, 0), (1, 2, -4, 0), (3, 2, -4, 0));
    public static readonly Pose[] Dash = Seq(64, 128, (1, 2, -8, 0), (2, 2, -8, 0), (1, 2, -8, 0), (3, 2, -8, 0));
    public static readonly Pose[] Sit = Seq(64, 128, (11, 250, 0, 0));
    public static readonly Pose[] SitLookUp = Seq(64, 128, (26, 250, 0, 0));
    public static readonly Pose[] SitDangleLegs = Seq(64, 112, (31, 5, 0, 0), (32, 15, 0, 0), (31, 5, 0, 0), (33, 15, 0, 0));
    public static readonly Pose[] Sprawl = Seq(64, 128, (21, 250, 0, 0));
    public static readonly Pose[] Creep = Seq(64, 128, (20, 28, 0, 0), (20, 4, -2, 0), (21, 4, -2, 0), (21, 4, -1, 0), (21, 24, 0, 0));

    // ---- walls and ceilings --------------------------------------------------------------------
    public static readonly Pose[] GrabWall = Seq(64, 128, (13, 250, 0, 0));
    public static readonly Pose[] ClimbWallUp = Seq(64, 128,
        (14, 16, 0, 0), (14, 4, 0, -1), (12, 4, 0, -1), (13, 4, 0, -1),
        (13, 16, 0, 0), (13, 4, 0, -2), (12, 4, 0, -2), (14, 4, 0, -2));
    public static readonly Pose[] ClimbWallDown = Seq(64, 128,
        (14, 16, 0, 0), (14, 4, 0, 2), (12, 4, 0, 2), (13, 4, 0, 2),
        (13, 16, 0, 0), (13, 4, 0, 1), (12, 4, 0, 1), (14, 4, 0, 1));
    public static readonly Pose[] GrabCeiling = Seq(64, 48, (23, 250, 0, 0));
    public static readonly Pose[] ClimbCeiling = Seq(64, 48,
        (25, 16, 0, 0), (25, 4, -1, 0), (23, 4, -1, 0), (24, 4, -1, 0),
        (24, 16, 0, 0), (24, 4, -2, 0), (23, 4, -2, 0), (25, 4, -2, 0));

    // ---- in the air ----------------------------------------------------------------------------
    public static readonly Pose[] Fall = Seq(64, 128, (4, 250, 0, 0));
    public static readonly Pose[] Jump = Seq(64, 128, (22, 250, 0, 0));
    public static readonly Pose[] Bounce = Seq(64, 128, (18, 4, 0, 0), (19, 4, 0, 0));

    // ---- being held ----------------------------------------------------------------------------
    /// <summary>The swing frame while dragged: <paramref name="footX"/> is where the pet's feet are, <paramref name="cursorX"/> the cursor.</summary>
    public static int Pinched(double footX, double cursorX) =>
        footX < cursorX - 50 ? 9 : footX < cursorX - 30 ? 7 : footX < cursorX + 30 ? 1 : footX < cursorX + 50 ? 8 : 10;

    /// <summary>Struggling while held still: 5 and 6 alternate, with a calm frame 1 now and then.</summary>
    public static readonly Pose[] Resist = Seq(64, 128,
        (5, 5, 0, 0), (6, 5, 0, 0), (5, 5, 0, 0), (6, 5, 0, 0), (1, 50, 0, 0),
        (5, 5, 0, 0), (6, 5, 0, 0), (5, 5, 0, 0), (6, 5, 0, 0), (5, 5, 0, 0), (6, 5, 0, 0), (1, 100, 0, 0));

    // ---- which frames a pack needs -------------------------------------------------------------
    /// <summary>A pack without all of these is rejected: stand, walk, fall and everything used while dragged.</summary>
    public static readonly int[] Required = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    public static readonly int[] ClimbFrames = [12, 13, 14];
    public static readonly int[] CeilingFrames = [23, 24, 25];
    public static readonly int[] SitFrames = [11];
    /// <summary>Sleeping lies on the Sprawl frame.</summary>
    public static readonly int[] SleepFrames = [21];
}

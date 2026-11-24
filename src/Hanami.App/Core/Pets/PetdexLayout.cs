namespace Hanami.Core.Pets;

/// <summary>
/// The sprite sheet of a Petdex pet (petdex.dev): 8 columns of 192×208 px cells, 9 rows (v1, 1536×1872) or 11 rows
/// (v2, 1536×2288). Cell (row, col) is stored as frame <c>row * 8 + col + 1</c>, so frame numbers run up to 88.
/// Pure: works on pixels and a table of which cells have art, so it is tested without decoding any image.
/// Rows, top to bottom: idle, run right, run left, wave, jump, failed, waiting, running, review (v2 adds two rows of
/// turning). What a row shows varies between pets, so each row is given the job it most often suits, and the pet only
/// gets the ability (sitting, sleeping) when its row has frames.
/// </summary>
public static class PetdexLayout
{
    public const int Columns = 8, CellWidth = 192, CellHeight = 208, SheetWidth = Columns * CellWidth;
    public const int RowsV1 = 9, RowsV2 = 11;

    public const int Idle = 0, RunRight = 1, RunLeft = 2, Wave = 3, Jump = 4, Failed = 5, Waiting = 6, Running = 7, Review = 8, TurnA = 9, TurnB = 10;

    /// <summary>Bumped when the mapping below changes; a pack stored with an older number is rebuilt from its frames when it is read.</summary>
    public const int MappingVersion = 2;

    /// <summary>Pixels at or below this alpha count as transparent, so a faint halo doesn't make a cell "non-empty".</summary>
    public const int AlphaThreshold = 16;
    /// <summary>A cell needs this many visible pixels to count as a frame (a stray speck is not art).</summary>
    public const int MinVisiblePixels = 20;

    public static int FrameNumber(int row, int col) => row * Columns + col + 1;

    /// <summary>9 or 11 for a sheet of exactly the right size, else null.</summary>
    public static int? RowsFor(int width, int height) =>
        width != SheetWidth ? null : height == RowsV1 * CellHeight ? RowsV1 : height == RowsV2 * CellHeight ? RowsV2 : null;

    /// <summary>What was found on a sheet: which cells have art, and where the idle pose's feet are (in cell pixels).</summary>
    public sealed record Sheet(bool[,] Filled, int FeetX, int FeetY)
    {
        public int Rows => Filled.GetLength(0);
    }

    /// <summary>
    /// Looks at a decoded sheet (<paramref name="bgra"/>, 4 bytes per pixel, rows top to bottom). Null with an
    /// <paramref name="error"/> when the size is wrong. The feet are the bottom centre of the box around every visible
    /// pixel of the idle row's frames.
    /// </summary>
    public static Sheet? Analyze(byte[] bgra, int width, int height, out string? error)
    {
        error = null;
        if (RowsFor(width, height) is not { } rows)
        {
            error = $"the sprite sheet is {width}×{height}, not {SheetWidth}×{RowsV1 * CellHeight} or {SheetWidth}×{RowsV2 * CellHeight}";
            return null;
        }
        if (bgra.Length < (long)width * height * 4) { error = "the sprite sheet is incomplete"; return null; }

        var filled = new bool[rows, Columns];
        int minX = int.MaxValue, maxX = -1, maxY = -1;
        for (var row = 0; row < rows; row++)
            for (var col = 0; col < Columns; col++)
            {
                var count = 0;
                int cMinX = int.MaxValue, cMaxX = -1, cMaxY = -1;
                for (var y = 0; y < CellHeight; y++)
                {
                    var offset = (((long)row * CellHeight + y) * width + (long)col * CellWidth) * 4;
                    for (var x = 0; x < CellWidth; x++)
                    {
                        if (bgra[offset + x * 4 + 3] <= AlphaThreshold) continue;
                        count++;
                        if (x < cMinX) cMinX = x;
                        if (x > cMaxX) cMaxX = x;
                        if (y > cMaxY) cMaxY = y;
                    }
                }
                if (count < MinVisiblePixels) continue;
                filled[row, col] = true;
                if (row != Idle) continue;
                minX = Math.Min(minX, cMinX); maxX = Math.Max(maxX, cMaxX); maxY = Math.Max(maxY, cMaxY);
            }
        // Without idle art there are no feet to stand on; Build reports the missing row.
        return maxX < 0 ? new Sheet(filled, CellWidth / 2, CellHeight) : new Sheet(filled, (minX + maxX + 1) / 2, maxY + 1);
    }

    /// <summary>The result of <see cref="Build"/>: the frames to store, how they animate, and what the pet may do.</summary>
    public sealed record Built(IReadOnlyList<int> Frames, PetMapping Mapping, PetCapabilities Abilities);

    /// <summary>
    /// Turns a sheet into the pack's data: stand = idle; walk and run = run left, each frame with its run-right twin; a wall
    /// grab = the first run-left frame; wall climbing = run left; fall, jump and the five dragged frames = the jump row; a hard
    /// landing = the jump row's last frames; wave (a click or stroke) = the wave row; <b>sitting = the waiting row</b>;
    /// <b>sleeping and lying down (the dizzy landing) = the failed row</b>; struggling while held = the running row (else
    /// wave, else idle); the running, review and turning rows are extra things to do while standing (fidgets). A pet may sit when
    /// the waiting row has frames and sleep when the failed row has. Ceilings don't exist. Anchors are in image pixels. Null
    /// with an <paramref name="error"/> when idle, either run row or jump has no art.
    /// </summary>
    public static Built? Build(Sheet sheet, out string? error)
    {
        error = null;
        List<int> Row(int row) => row < sheet.Rows
            ? Enumerable.Range(0, Columns).Where(c => sheet.Filled[row, c]).Select(c => FrameNumber(row, c)).ToList()
            : new();

        var idle = Row(Idle); var runRight = Row(RunRight); var runLeft = Row(RunLeft);
        var jump = Row(Jump); var failed = Row(Failed); var wave = Row(Wave); var waiting = Row(Waiting);
        var running = Row(Running); var review = Row(Review); var turnA = Row(TurnA); var turnB = Row(TurnB);
        foreach (var (name, list) in new[] { ("idle", idle), ("run right", runRight), ("run left", runLeft), ("jump", jump) })
            if (list.Count == 0) { error = $"its sprite sheet has no “{name}” frames"; return null; }

        PoseDef Pose(int frame, int ticks, int vx = 0, int twin = 0) =>
            new() { Frame = frame, Ticks = ticks, AnchorX = sheet.FeetX, AnchorY = sheet.FeetY, Vx = vx, Twin = twin };
        List<PoseDef> Play(List<int> frames, int ticks, int vx = 0, List<int>? twins = null) =>
            frames.Select((f, i) => Pose(f, ticks, vx, twins is { Count: > 0 } ? twins[i % twins.Count] : 0)).ToList();

        var standing = Play(idle, 12);
        var sitting = waiting.Count > 0 ? Play(waiting, 12) : standing;
        var lying = failed.Count > 0 ? Play(failed, 8) : standing;
        var landing = jump.Count > 2 ? jump[^2..] : jump;
        var map = new Dictionary<string, List<PoseDef>>
        {
            ["stand"] = standing,
            ["walk"] = Play(runLeft, 4, -3, runRight),
            ["run"] = Play(runLeft, 3, -5, runRight),
            ["grabWall"] = Play([runLeft[0]], 250, 0, runRight),
            ["climbWallUp"] = Play(runLeft, 4, 0, runRight).Select(p => { p.Vy = -2; return p; }).ToList(),
            ["climbWallDown"] = Play(runLeft, 4, 0, runRight).Select(p => { p.Vy = 2; return p; }).ToList(),
            ["fall"] = Play(jump, 4),
            ["jump"] = Play(jump, 4),
            ["bounce"] = Play(landing, 5),
            ["resist"] = Play(running.Count > 0 ? running : wave.Count > 0 ? wave : idle, 5),
            ["wave"] = Play(wave.Count > 0 ? wave : idle, 5),
            ["sit"] = sitting, ["sitLookUp"] = sitting, ["sitDangle"] = sitting,
            ["sleep"] = lying, ["sprawl"] = lying,
            // Ceilings don't exist for these pets (see Abilities), but the animations stay on frames that exist.
            ["grabCeiling"] = standing, ["climbCeiling"] = standing,
        };
        var fidgets = new[] { running, review, turnA, turnB }.Where(r => r.Count > 0).Take(4).ToList();
        for (var i = 0; i < fidgets.Count; i++) map[$"fidget{i + 1}"] = Play(fidgets[i], 8);
        var swing = Enumerable.Range(0, 5).Select(i => jump[(int)Math.Round(i * (jump.Count - 1) / 4.0)]).ToList();

        var frames = new List<int>();
        for (var row = 0; row < sheet.Rows; row++) frames.AddRange(Row(row));
        return new Built(frames, new PetMapping { Animations = map, Swing = swing, ImagePixels = true },
            new PetCapabilities(Climb: true, Ceiling: false, Sit: waiting.Count > 0, Sleep: failed.Count > 0));
    }

    /// <summary>
    /// The pack's data again from the frames it already has (a Petdex pack stored before the mapping changed): the cells with art
    /// are known from the frame numbers, and the feet from the stand pose it had. Null when it lacks a required row.
    /// </summary>
    public static Built? Rebuild(IEnumerable<int> frames, int feetX, int feetY)
    {
        var numbers = frames.Where(n => n >= 1).ToList();
        if (numbers.Count == 0) return null;
        var rows = Math.Max(RowsV1, (numbers.Max() - 1) / Columns + 1);
        var filled = new bool[rows, Columns];
        foreach (var n in numbers) filled[(n - 1) / Columns, (n - 1) % Columns] = true;
        return Build(new Sheet(filled, feetX, feetY), out _);
    }
}

using System;
using System.Linq;

namespace Trifle.Visuals;

// Left and Width are fractions of the displayed keyboard width.
public readonly record struct PianoKey(int Pitch, bool IsBlack, double Left, double Width);

public sealed class KeyboardLayout
{
    private static readonly int[] WhitePitchClasses = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly string[] PitchNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public int FirstPitch { get; }
    public int LastPitch { get; }
    public PianoKey[] Keys { get; }

    public KeyboardLayout(int firstPitch = 21, int lastPitch = 108)
    {
        if (firstPitch < 0 || lastPitch > 127 || firstPitch > lastPitch)
            throw new ArgumentOutOfRangeException(nameof(firstPitch), "琴键范围需要满足 0 ≤ 起点 ≤ 终点 ≤ 127。");
        FirstPitch = firstPitch;
        LastPitch = lastPitch;

        var rawKeys = Enumerable.Range(firstPitch, lastPitch - firstPitch + 1).Select(pitch =>
        {
            bool black = IsBlack(pitch);
            int before = pitch / 12 * 7 + WhitePitchClasses.Count(value => value < pitch % 12);
            return new PianoKey(pitch, black, black ? before - 0.31 : before, black ? 0.62 : 1.0);
        }).ToArray();
        double left = rawKeys.Min(key => key.Left);
        double span = rawKeys.Max(key => key.Left + key.Width) - left;
        Keys = rawKeys.Select(key => key with
        {
            Left = (key.Left - left) / span,
            Width = key.Width / span
        }).ToArray();
    }

    public bool TryGetKey(int pitch, out PianoKey key)
    {
        if (pitch < FirstPitch || pitch > LastPitch)
        {
            key = default;
            return false;
        }
        key = Keys[pitch - FirstPitch];
        return true;
    }

    public static bool IsBlack(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;
    public static string PitchName(int pitch) => PitchNames[pitch % 12] + (pitch / 12 - 1);
}

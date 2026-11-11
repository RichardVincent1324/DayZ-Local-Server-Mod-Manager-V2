namespace DayZModManager.Core;

/// <summary>
/// Compares strings so that embedded digit runs sort numerically ("10" after
/// "9") instead of lexicographically. Used for type-file ordering both in the
/// DataGrid and in the generated cfgeconomycore.xml block.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0;
        int j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int startX = i;
                int startY = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }

                ReadOnlySpan<char> digitsX = x.AsSpan(startX, i - startX).TrimStart('0');
                ReadOnlySpan<char> digitsY = y.AsSpan(startY, j - startY).TrimStart('0');

                int lengthCompare = digitsX.Length.CompareTo(digitsY.Length);
                if (lengthCompare != 0)
                {
                    return lengthCompare;
                }

                int digitCompare = digitsX.SequenceCompareTo(digitsY);
                if (digitCompare != 0)
                {
                    return digitCompare;
                }

                continue;
            }

            int charCompare = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (charCompare != 0)
            {
                return charCompare;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}

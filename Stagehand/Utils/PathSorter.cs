using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Stagehand.Utils;

/// <summary>
/// A string comparer for ordering paths alphabetically with all subdirectories ordered before files.
/// </summary>
internal class PathSorter : IComparer<string>
{
    public static PathSorter CurrentCulture { get; } = new PathSorter(StringComparer.CurrentCulture);
    public static PathSorter CurrentCultureIgnoreCase { get; } = new PathSorter(StringComparer.CurrentCultureIgnoreCase);
    public static PathSorter InvariantCulture { get; } = new PathSorter(StringComparer.InvariantCulture);
    public static PathSorter InvariantCultureIgnoreCase { get; } = new PathSorter(StringComparer.InvariantCultureIgnoreCase);
    public static PathSorter Ordinal { get; } = new PathSorter(StringComparer.Ordinal);
    public static PathSorter OrdinalIgnoreCase { get; } = new PathSorter(StringComparer.OrdinalIgnoreCase);

    private readonly StringComparer _nameComparer;

    /// <summary>
    /// Creates a new path sorter using the given string comparer to compare individual files and directories.
    /// </summary>
    public PathSorter(StringComparer nameComparer)
    {
        _nameComparer = nameComparer;
    }

    public int Compare(string? x, string? y)
    {
        if (x == null)
        {
            return -1;
        }

        if (y == null)
        {
            return 1;
        }

        // A differing leaf is where there is no slash after the first differing character.
        // If one of the strings is a differing leaf and the other isn't, the differing leaf goes afterwards.

        int indexOfFirstMismatch = 0;

        for (indexOfFirstMismatch = 0; indexOfFirstMismatch < x.Length && indexOfFirstMismatch < y.Length; indexOfFirstMismatch++)
        {
            if (x[indexOfFirstMismatch] != y[indexOfFirstMismatch])
            {
                break;
            }
        }

        bool firstLeaf = x.LastIndexOf(Path.DirectorySeparatorChar) < indexOfFirstMismatch;
        bool secondLeaf = y.LastIndexOf(Path.DirectorySeparatorChar) < indexOfFirstMismatch;

        if (firstLeaf && !secondLeaf)
        {
            return 1;
        }
        else if (secondLeaf && !firstLeaf)
        {
            return -1;
        }
        else
        {
            return _nameComparer.Compare(x, y);
        }
    }
}

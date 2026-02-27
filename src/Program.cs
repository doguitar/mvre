using System.Text.RegularExpressions;

string sourceFolder = ".";
string targetFolder = ".";

bool recursive = true;
bool filesOnly = true;
bool directoriesOnly = false;
bool createParents = false;

bool noClobber = false;
string? overwriteMode = null;
bool verbose = false;
bool dryRun = false;

string? matchPattern = null;
string? renamePattern = null;

bool filesOnlySpecified = false;
bool directoriesOnlySpecified = false;

if (args.Length >= 1 && (args.Contains("--help") || args.Contains("-h")))
{
    ShowHelp();
    return 0;
}

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            ShowHelp();
            return 0;
        case "--source-folder" or "-s":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Missing value for --source-folder/-s.");
                return 1;
            }
            sourceFolder = args[++i];
            break;
        case "--target-folder" or "-t":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Missing value for --target-folder/-t.");
                return 1;
            }
            targetFolder = args[++i];
            break;
        case "--no-clobber" or "-n":
            noClobber = true;
            break;
        case "--overwrite-mode" or "-o":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Missing value for --overwrite-mode/-o.");
                return 1;
            }
            overwriteMode = args[++i];
            break;
        case "--recuresive" or "--recursive" or "-r":
            recursive = true;
            break;
        case "--files-only" or "-f":
            filesOnly = true;
            filesOnlySpecified = true;
            break;
        case "--directories-only" or "-d":
            directoriesOnly = true;
            directoriesOnlySpecified = true;
            break;
        case "--create-parents" or "-p":
            createParents = true;
            break;
        case "--verbose" or "-v":
            verbose = true;
            break;
        case "--dry-run":
            dryRun = true;
            break;
        default:
            if (args[i].StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                return 1;
            }
            if (matchPattern is null)
                matchPattern = args[i];
            else if (renamePattern is null)
                renamePattern = args[i];
            break;
    }
}

if (matchPattern is null || renamePattern is null)
{
    ShowHelp();
    return 1;
}

if (noClobber && overwriteMode is not null)
{
    Console.Error.WriteLine("Cannot use both --no-clobber/-n and --overwrite-mode/-o.");
    return 1;
}

if (filesOnlySpecified && directoriesOnlySpecified)
{
    Console.Error.WriteLine("Cannot use both --files-only/-f and --directories-only/-d.");
    return 1;
}

if (directoriesOnlySpecified)
{
    directoriesOnly = true;
    filesOnly = false;
}
else if (filesOnlySpecified)
{
    filesOnly = true;
    directoriesOnly = false;
}

if (!filesOnly && !directoriesOnly)
{
    Console.Error.WriteLine("Must specify either --files-only/-f or --directories-only/-d.");
    return 1;
}

Regex regex;
try
{
    regex = new Regex(matchPattern);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Invalid matching regex: {ex.Message}");
    return 1;
}

string sourceFull = Path.GetFullPath(sourceFolder);
string targetFull = Path.GetFullPath(targetFolder);

if (!Directory.Exists(sourceFull))
{
    Console.Error.WriteLine($"Directory not found: {sourceFull}");
    return 1;
}

if (!Directory.Exists(targetFull))
{
    Console.Error.WriteLine($"Directory not found: {targetFull}");
    return 1;
}

overwriteMode = overwriteMode?.Trim().ToLowerInvariant();
if (overwriteMode is not null && overwriteMode is not ("larger" or "small" or "older" or "newer"))
{
    Console.Error.WriteLine("Invalid value for --overwrite-mode/-o. Use one of: larger, small, older, newer.");
    return 1;
}

var moves = new List<(string From, string To, bool IsDirectory, string FromRel, string ToRel)>();

SearchOption search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

IEnumerable<string> candidates;
if (filesOnly)
{
    candidates = Directory.EnumerateFiles(sourceFull, "*", search);
}
else
{
    candidates = Directory.EnumerateDirectories(sourceFull, "*", search)
        .OrderByDescending(p => p.Length);
}

foreach (var fromPath in candidates)
{
    string fromFullPath = Path.GetFullPath(fromPath);
    string rel = Path.GetRelativePath(sourceFull, fromFullPath);
    string relNormalized = NormalizeRelativeForRegex(rel);

    var match = regex.Match(relNormalized);
    if (!match.Success)
    {
        if (verbose)
            Console.WriteLine($"{rel}: pattern did not match");
        continue;
    }

    string toRelNormalized;
    try
    {
        toRelNormalized = regex.Replace(relNormalized, renamePattern, 1);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Invalid rename regex for '{rel}': {ex.Message}");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(toRelNormalized))
    {
        if (verbose)
            Console.WriteLine($"{rel}: replacement is empty");
        continue;
    }

    string toRel = DenormalizeRelativePath(toRelNormalized);
    if (Path.IsPathRooted(toRel))
    {
        Console.Error.WriteLine($"Rename result must be a relative path. Got: '{toRelNormalized}'");
        return 1;
    }

    string toFullPath = Path.GetFullPath(Path.Combine(targetFull, toRel));
    if (!IsUnderDirectory(toFullPath, targetFull))
    {
        Console.Error.WriteLine($"Rename result escapes target folder: '{toRelNormalized}'");
        return 1;
    }

    if (PathsEqual(fromFullPath, toFullPath))
    {
        if (verbose)
            Console.WriteLine($"{rel}: source and target are the same");
        continue;
    }

    bool isDir = directoriesOnly;
    if (isDir && IsUnderDirectory(toFullPath, fromFullPath))
    {
        Console.Error.WriteLine($"Cannot move a directory into itself: '{rel}' -> '{toRelNormalized}'");
        return 1;
    }
    moves.Add((From: fromFullPath, To: toFullPath, IsDirectory: isDir, FromRel: rel, ToRel: toRel));
}

if (moves.Count == 0)
    return 0;

// Resolve duplicate targets: when overwrite-mode is set, pick one winner per destination
var pathComparer = StringComparer.OrdinalIgnoreCase;
var movesWithIndex = moves.Select((m, i) => (Move: m, Index: i)).ToList();
var duplicateGroups = movesWithIndex
    .GroupBy(t => t.Move.To, pathComparer)
    .Where(g => g.Count() > 1)
    .ToList();

if (duplicateGroups.Count > 0)
{
    if (overwriteMode is null)
    {
        Console.Error.WriteLine("Multiple sources map to the same target path:");
        foreach (var g in duplicateGroups)
            Console.Error.WriteLine($"  {g.Key}");
        return 1;
    }

    var indicesToDrop = new HashSet<int>();
    foreach (var group in duplicateGroups)
    {
        var list = group.ToList();
        int winnerIdx = list[0].Index;
        for (int k = 1; k < list.Count; k++)
        {
            int candidateIdx = list[k].Index;
            if (PickWinner(moves[winnerIdx], moves[candidateIdx], overwriteMode) == candidateIdx)
                winnerIdx = candidateIdx;
        }
        foreach (var t in list)
        {
            if (t.Index != winnerIdx)
                indicesToDrop.Add(t.Index);
        }
    }
    if (verbose)
    {
        foreach (var t in movesWithIndex.Where(t => indicesToDrop.Contains(t.Index)))
            Console.WriteLine($"{t.Move.FromRel}: not moved (another source was chosen for same destination)");
    }
    moves = movesWithIndex.Where(t => !indicesToDrop.Contains(t.Index)).Select(t => t.Move).ToList();
}

var moveOrder = GetSafeMoveOrder(moves);

foreach (int i in moveOrder)
{
    var (fromPath, toPath, isDir, fromRel, toRel) = moves[i];
    try
    {
        if (isDir)
        {
            if (!Directory.Exists(fromPath))
            {
                Console.Error.WriteLine($"Source directory not found: {fromPath}");
                return 1;
            }

            if (File.Exists(toPath) || Directory.Exists(toPath))
            {
                if (noClobber)
                {
                    if (verbose)
                        Console.WriteLine($"{fromRel}: not moved (target exists, no-clobber)");
                    continue;
                }

                Console.Error.WriteLine($"Target already exists: {toPath}");
                return 1;
            }

            if (createParents && !dryRun)
            {
                var parent = Path.GetDirectoryName(toPath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);
            }

            if (!dryRun)
                Directory.Move(fromPath, toPath);
            Console.WriteLine($"{fromRel} -> {toRel}");
            continue;
        }

        if (!File.Exists(fromPath))
        {
            Console.Error.WriteLine($"Source file not found: {fromPath}");
            return 1;
        }

        if (Directory.Exists(toPath))
        {
            Console.Error.WriteLine($"Target is a directory: {toPath}");
            return 1;
        }

        if (createParents && !dryRun)
        {
            var parent = Path.GetDirectoryName(toPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
        }

        bool targetExists = File.Exists(toPath);
        if (!targetExists)
        {
            if (!dryRun)
                File.Move(fromPath, toPath);
            Console.WriteLine($"{fromRel} -> {toRel}");
            continue;
        }

        if (noClobber)
        {
            if (verbose)
                Console.WriteLine($"{fromRel}: not moved (target exists, no-clobber)");
            continue;
        }

        bool shouldOverwrite = overwriteMode is null;
        if (overwriteMode is not null)
        {
            var src = new FileInfo(fromPath);
            var dst = new FileInfo(toPath);

            shouldOverwrite = overwriteMode switch
            {
                "larger" => src.Length > dst.Length,
                "small" => src.Length < dst.Length,
                "older" => src.LastWriteTimeUtc < dst.LastWriteTimeUtc,
                "newer" => src.LastWriteTimeUtc > dst.LastWriteTimeUtc,
                _ => false
            };
        }

        if (!shouldOverwrite)
        {
            if (verbose)
                Console.WriteLine($"{fromRel}: not moved (target exists, overwrite condition not met)");
            continue;
        }

        if (!dryRun)
            File.Move(fromPath, toPath, overwrite: true);
        Console.WriteLine($"{fromRel} -> {toRel}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to move '{fromPath}' -> '{toPath}': {ex.Message}");
        return 1;
    }
}

return 0;

static void ShowHelp()
{
    var stdout = Console.Out;
    stdout.WriteLine("mvre - Move or rename files and folders using regular expressions.");
    stdout.WriteLine();
    stdout.WriteLine("USAGE");
    stdout.WriteLine("  mvre [options] <matching-regex> <replacement>");
    stdout.WriteLine();
    stdout.WriteLine("ARGUMENTS");
    stdout.WriteLine("  <matching-regex>    .NET-style regex to match relative paths under the source folder.");
    stdout.WriteLine("  <replacement>       Replacement string. Use $1, $2, ... for captured groups; ${name} for named groups.");
    stdout.WriteLine();
    stdout.WriteLine("OPTIONS");
    stdout.WriteLine("  -s, --source-folder <path>     Source directory (default: current directory).");
    stdout.WriteLine("  -t, --target-folder <path>     Target directory (default: current directory).");
    stdout.WriteLine("  -n, --no-clobber               Do not overwrite existing files. Incompatible with -o.");
    stdout.WriteLine("  -o, --overwrite-mode <mode>    On conflict, overwrite when source is: larger, small, older, or newer. Incompatible with -n.");
    stdout.WriteLine("  -r, --recursive                Recurse into subdirectories (default).");
    stdout.WriteLine("  -f, --files-only               Apply only to files (default).");
    stdout.WriteLine("  -d, --directories-only         Apply only to directories.");
    stdout.WriteLine("  -p, --create-parents           Create parent directories in the target if they do not exist.");
    stdout.WriteLine("  -v, --verbose                  Print a line for every file found; if not moved, show the reason.");
    stdout.WriteLine("      --dry-run                  Do not move or create anything; only report what would be done.");
    stdout.WriteLine("  -h, --help                     Show this help.");
    stdout.WriteLine();
    stdout.WriteLine("EXAMPLES");
    stdout.WriteLine("  mvre \"(.+)\\.txt\" \"$1.bak\"");
    stdout.WriteLine("  mvre -s ./in -t ./out \"^(.+)\\.jpg$\" \"photos/$1.jpg\"");
}

static string NormalizeRelativeForRegex(string relativePath)
{
    return relativePath.Replace('\\', '/');
}

static string DenormalizeRelativePath(string normalizedPath)
{
    return normalizedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}

static bool PathsEqual(string a, string b)
{
    return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);
}

static string EnsureTrailingSeparator(string path)
{
    if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        return path;
    return path + Path.DirectorySeparatorChar;
}

static bool IsUnderDirectory(string candidatePath, string directoryPath)
{
    string dir = EnsureTrailingSeparator(Path.GetFullPath(directoryPath));
    string cand = Path.GetFullPath(candidatePath);
    return cand.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || string.Equals(cand, directoryPath, StringComparison.OrdinalIgnoreCase);
}

// Returns 0 if move1 wins, 1 if move2 wins (when multiple sources map to same destination and overwrite-mode is set).
static int PickWinner(
    (string From, string To, bool IsDirectory, string FromRel, string ToRel) move1,
    (string From, string To, bool IsDirectory, string FromRel, string ToRel) move2,
    string overwriteMode)
{
    bool secondWins = false;
    if (move1.IsDirectory || move2.IsDirectory)
    {
        var t1 = Directory.Exists(move1.From) ? Directory.GetLastWriteTimeUtc(move1.From) : DateTime.MinValue;
        var t2 = Directory.Exists(move2.From) ? Directory.GetLastWriteTimeUtc(move2.From) : DateTime.MinValue;
        secondWins = overwriteMode switch
        {
            "larger" => t2 > t1,
            "small" => t2 < t1,
            "older" => t2 < t1,
            "newer" => t2 > t1,
            _ => false
        };
    }
    else
    {
        var f1 = new FileInfo(move1.From);
        var f2 = new FileInfo(move2.From);
        if (!f1.Exists) return 1;
        if (!f2.Exists) return 0;
        secondWins = overwriteMode switch
        {
            "larger" => f2.Length > f1.Length,
            "small" => f2.Length < f1.Length,
            "older" => f2.LastWriteTimeUtc < f1.LastWriteTimeUtc,
            "newer" => f2.LastWriteTimeUtc > f1.LastWriteTimeUtc,
            _ => false
        };
    }
    return secondWins ? 1 : 0;
}

static List<int> GetSafeMoveOrder(List<(string From, string To, bool IsDirectory, string FromRel, string ToRel)> moves)
{
    var comparer = StringComparer.OrdinalIgnoreCase;
    var remaining = new HashSet<int>(Enumerable.Range(0, moves.Count));
    var remainingFrom = new HashSet<string>(moves.Select(m => m.From), comparer);
    var order = new List<int>(moves.Count);

    while (order.Count < moves.Count)
    {
        bool any = false;

        // pick any move whose target isn't the source of another remaining move
        foreach (int i in remaining.ToList())
        {
            if (!remainingFrom.Contains(moves[i].To))
            {
                remaining.Remove(i);
                remainingFrom.Remove(moves[i].From);
                order.Add(i);
                any = true;
            }
        }

        if (!any)
            break; // cycle or overlap; fall back to input order
    }

    if (order.Count != moves.Count)
        return Enumerable.Range(0, moves.Count).ToList();

    return order;
}

# mvre

A .NET 10 command-line tool to **move** or **rename** files and folders using regular expressions. Match paths under a source folder and write them to a target folder with a replacement pattern.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build

```bash
dotnet build src/mvre.csproj
```

## Publish (Native AOT)

Native AOT produces a single, self-contained native executable.

### Windows (x64)

```bash
dotnet publish src/mvre.csproj -c ReleaseAot -r win-x64
```

Output:

```text
src/bin/ReleaseAot/net10.0/win-x64/publish/
```

### Linux (x64)

```bash
dotnet publish src/mvre.csproj -c ReleaseAot -r linux-x64
```

## Usage

```text
mvre [options] <matching-regex> <replacement>
```

- **&lt;matching-regex&gt;** — .NET-style regex applied to the relative path under the source folder (forward slashes).
- **&lt;replacement&gt;** — Replacement string. Use `$1`, `$2`, … for captured groups and `${name}` for named groups.

### Options

| Option | Short | Description |
|--------|--------|-------------|
| `--source-folder` | `-s` | Source directory (default: current directory). |
| `--target-folder` | `-t` | Target directory (default: current directory). |
| `--no-clobber` | `-n` | Do not overwrite existing files. Incompatible with `-o`. |
| `--overwrite-mode` | `-o` | On conflict, overwrite when source is: `larger`, `small`, `older`, or `newer`. Incompatible with `-n`. |
| `--recursive` | `-r` | Recurse into subdirectories (default). |
| `--files-only` | `-f` | Apply only to files (default). |
| `--directories-only` | `-d` | Apply only to directories. |
| `--create-parents` | `-p` | Create parent directories in the target if they do not exist. |
| `--help` | `-h` | Show help. |

### Examples

Rename `.txt` to `.bak` in the current directory:

```bash
mvre "(.+)\.txt" "$1.bak"
```

Move `.jpg` files from `./in` to `./out/photos/`:

```bash
mvre -s ./in -t ./out "^(.+)\.jpg$" "photos/$1.jpg"
```

Show help:

```bash
mvre -h
```

## Regex syntax

- **Matching:** [.NET regular expressions](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions) (Regex class).
- **Replacement:** Use `$1`, `$2`, … for groups by index and `${groupname}` for named groups.

## License

See repository.

# devtools

Installs development tools and switches between their versions, on Windows (cmd) and Linux (bash, zsh).

Every tool lives in one directory, each version in its own subdirectory. devtools puts the selected versions on `PATH`
and sets their variables, such as `JAVA_HOME`, in every new console.

```
D:\Tools
├─ devtools\        devtools itself
├─ java\
│  ├─ .app          what java puts on PATH, which variables it sets and how versions are installed
│  ├─ 17.0.7\
│  └─ 25.0.5\
└─ go\ ...
```

## Setup

1. Download `devtools-<version>-win-x64.zip` or `devtools-<version>-linux-x64.tar.gz` from
   [Releases](https://github.com/bitjuice-net/bitjuice-devtools/releases) and extract it into a `devtools` directory
   inside your tools directory, e.g. `D:\Tools\devtools`. It needs the .NET 10 runtime.
2. Load devtools in every new console:
   ```
   devtools shell install
   ```
   On Windows this adds a command to the cmd AutoRun, elsewhere a line to `~/.bashrc` or `~/.zshrc`.
   `devtools shell uninstall` removes it again.
3. Open a new console. It has the `dt` command, which runs devtools and applies the changes to the console right away.

The tools directory is the parent of the devtools directory. Another one can be set in `data/settings.json`,
see `data/settings.example.json`.

## Everyday use

```
dt ls                   list applications, selected and installed versions
dt ls java              details: versions, paths, variables, where the manifests come from
dt sl java 17.0.7       select a version, also latest or oldest
dt sl java              choose the version from a list
dt app install java     install the newest version, or a given one: dt app install java 21.0.2
dt app update java      install the newest version when it is newer than the installed ones
dt app update           the same for every application that can be installed
dt app reset java       replace .app with the one from its manifest repository
dt app remove java 17.0.7
dt app disable java     take an application off PATH without deleting it, `dt app enable java` puts it back
dt sc                   scan the tools directory now
```

Versions added or deleted by hand and edited `.app` files are found on their own, a scan is rarely needed.
`install` and `update` use the versions found in the last hour, `--refresh` asks the version sources again.

| Group | Commands |
|---|---|
| `app` | `list` (`ls`), `select` (`s`), `enable` (`e`), `disable` (`d`), `install`, `update`, `remove` (`rm`), `reset` |
| `cache` | `scan`, `prune` |
| `source` | `add`, `remove` (`rm`), `list` (`ls`), `sync` |
| `shell` | `install`, `uninstall`, `hook`, `env` |

`ls`, `sl` and `sc` are shortcuts for `app list`, `app select` and `cache scan`. `devtools <command> -h` describes every command.

## Manifests

Every application has a `.app` in its directory. The tools directory alone is enough to rebuild the caches in `data`.

```json
{
  "name": "age",
  "revision": 1,
  "versioned": true,
  "paths": ["%APPDIR%"],
  "envs": { "AGE_HOME": "%APPDIR%" },
  "install": {
    "versions": { "type": "github-releases", "repo": "FiloSottile/age", "tag": "^v(?<version>\\d+\\.\\d+\\.\\d+)$" },
    "archNames": { "x64": "amd64" },
    "steps": [
      { "type": "download", "os": "windows", "asset": "age-v{version}-{os}-{arch}.zip" },
      { "type": "download", "os": "linux", "asset": "age-v{version}-{os}-{arch}.tar.gz" },
      { "type": "extract", "stripRoot": true }
    ]
  }
}
```

- `versioned`: every subdirectory is a version. Without it the directory itself is the application.
- `paths` go on `PATH`, `envs` are set as variables.
- `%APPDIR%` is the directory of the selected version, `%HOME%` and `%USERPROFILE%` the home of the user on every system,
  other `%NAME%` come from the environment. Paths may use `\`, it becomes `/` outside of Windows.
- `revision` is the version of the manifest, bumped in a manifest repository whenever it changes.
- `install` is optional, without it versions are added by hand.
- `origin` is added by devtools to a manifest fetched from a repository: the repository and the SHA-256 of the content.

`install`:

- `versions.type`: `github-releases` and `github-tags` (with `repo` and an optional `tag` pattern), or `web-page`
  (with `url` and a `pattern` that finds the download links).
- Steps: `download` (an `asset` of the release or a `url`, optionally `to`), `extract` (zip, tar.gz, tgz, 7z,
  `stripRoot` drops a single root directory) and `move` (`from`, `to`).
- `{version}`, `{tag}`, `{os}` (`windows`, `linux`, `macos`) and `{arch}` (`x64`, `arm64`, ...) are replaced in steps
  and in `versions.url` and `versions.pattern`. `osNames` and `archNames` map them to the names a project uses.
  `os` and `arch` on a step run it only on that system or architecture.

GitHub allows 60 API requests an hour without a token. A token in `data/settings.json` raises the limit and opens private
manifest repositories, the `GITHUB_TOKEN` variable is used when the settings have none. A token without any scopes is
enough for public repositories.

```json
{ "githubToken": "github_pat_..." }
```

## Manifest repositories

`dt app install` gets the `.app` of applications you do not have yet from manifest repositories,
[bitjuice-net/bitjuice-devtools-apps](https://github.com/bitjuice-net/bitjuice-devtools-apps) by default.

```
dt source list                       repositories and how many applications they have
dt source list owner/repo            the applications of a repository and their revisions
dt source add owner/repo             add a repository on GitHub, or a full local path while writing manifests
dt source sync                       get the newest lists of applications of all repositories now
```

A repository has a directory per application with its `.app`, and `index.json` in its root with the revision
of every application:

```json
{ "apps": { "age": { "revision": 2 }, "java": { "revision": 1 } } }
```

A list older than a day is downloaded again when `dt app install` or `dt app update` needs it, an unavailable
repository keeps the previous one. These commands bring the manifest of an application to the revision on the list:

- An application gets the manifest of the repository with the newest revision and stays with the repository in its `origin`.
- A manifest without `origin` counts as its own `revision`, 1 without one, and is replaced only by a newer one.
- A fetched manifest changed by hand is kept, `dt ls <app>` shows it as changed locally.
  `dt app reset <app>` replaces it with the one from the repository.

## Data

Settings and state are in `data` next to devtools:

| File | |
|---|---|
| `settings.json` | settings, e.g. `rootPath` |
| `tools.json` | selected and disabled versions |
| `cache.json` | applications found in the tools directory |
| `scan.json` | times of the scanned directories and `.app` files, a change makes the next command scan again |
| `versions.json` | versions found by the version sources, kept for an hour |
| `sources.json` | manifest repositories |
| `indexes.json` | the lists of the repositories, downloaded again after a day or by `dt source sync` |
| `state` | changes on every save, `dt` reloads the console only when it changed |

## Building

```
dotnet test DevTools.sln
./publish.sh 1.2.3        # publish/win-x64 and publish/linux-x64
```

Pushing a tag such as `v1.2.3` builds both packages and creates a GitHub release.

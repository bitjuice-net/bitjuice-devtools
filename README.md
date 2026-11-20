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

After every change devtools writes `cache/env.cmd` (Windows) or `cache/env.sh` with PATH and the variables of the selected
tools. A new console and `dt` only run that script, devtools itself is not started for it. `%NAME%` in a manifest is
replaced when the script is written, with the variables devtools had then.

The tools directory is the parent of the devtools directory. Another one can be set in `config/settings.json`,
see `config/settings.json.example`.

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

After adding or deleting versions by hand, or editing `.app`, run `dt sc`. A command that misses an application or
a version, such as `dt sl java 26.0.1` right after copying it in, scans once on its own.
`install` and `update` use the versions found in the last hour, `--refresh` asks the version sources again.

| Group | Commands |
|---|---|
| `app` | `list` (`ls`), `select` (`s`), `enable` (`e`), `disable` (`d`), `install`, `update`, `remove` (`rm`), `reset` |
| `cache` | `scan`, `prune` |
| `source` | `add`, `remove` (`rm`), `list` (`ls`) |
| `shell` | `install`, `uninstall`, `hook`, `env` |

`ls`, `sl` and `sc` are shortcuts for `app list`, `app select` and `cache scan`. `devtools <command> -h` describes every command.

## Manifests

Every application has a `.app` in its directory. The tools directory alone is enough to rebuild `cache`.

```json
{
  "name": "age",
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

- Every subdirectory named like a version (`1.2.0`, `21.0.2`) is a version of the application. `"flat": true` means
  the application lies right in its directory, without versions.
- `paths` go on `PATH`, `envs` are set as variables.
- `%APPDIR%` is the directory of the selected version, `%HOME%` and `%USERPROFILE%` the home of the user on every system,
  other `%NAME%` come from the environment. Paths may use `\`, it becomes `/` outside of Windows.
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

GitHub allows 60 API requests an hour without a token. A token in `config/settings.json` raises the limit and opens private
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
dt source list owner/repo            the applications of a repository and which of them you have
dt source add owner/repo             add a repository on GitHub, or a full local path while writing manifests
```

A repository has `apps/<application>.app` for every application, and `index.json` in its root with their names:

```json
{ "apps": ["age", "java"] }
```

Nothing of a repository is cached. `dt app install` and `dt app update` read it every time and bring the manifest of
the application up to date:

- An application stays with the repository in its `origin`. A new one comes from the only repository that has it.
  When more than one has it, devtools does not choose: `--source owner/repo` tells which one.
- A manifest without `origin`, made by hand, is never replaced on its own. `dt app reset <app>` replaces it.
- A fetched manifest changed by hand is kept, `dt ls <app>` shows it as changed locally.
  `dt app reset <app>` replaces it with the one from the repository, `--source` with the one from another repository.

## Configuration and cache

Next to devtools, `config` keeps what you chose and `cache` what devtools can rebuild:

| File | |
|---|---|
| `config/settings.json` | settings, e.g. `rootPath` and `githubToken` |
| `config/apps.json` | selected and disabled versions |
| `config/sources.json` | manifest repositories |
| `cache/apps.json` | applications found in the tools directory |
| `cache/versions.json` | versions found by the version sources, kept for an hour |
| `cache/state` | changes on every save, `dt` reloads the console only when it changed |
| `cache/env.cmd`, `cache/env.sh` | PATH and variables of the selected tools, run by new consoles and `dt` |

Any file in `cache`, or the whole directory, may be deleted, the next command rebuilds it.

## Building

```
dotnet test DevTools.sln
./publish.sh 1.2.3        # publish/win-x64 and publish/linux-x64
```

Pushing a tag such as `v1.2.3` builds both packages and creates a GitHub release.

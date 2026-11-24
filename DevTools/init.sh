# Loads the selected tools into bash or zsh (PATH and the variables of cache/env.sh) and defines `dt`.
# ~/.bashrc or ~/.zshrc sources it, see `devtools shell install`, it can also be sourced by hand: . /path/to/devtools/init.sh
# dt is a function because a child process cannot change the shell. A missing or outdated env.sh is written by devtools.

if [ -n "${ZSH_VERSION-}" ]; then
  eval '_dt_home=${${(%):-%x}:A:h}'
  _dt_shell=zsh
else
  _dt_home=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
  _dt_shell=bash
fi

_dt_reload() {
  local state=
  [ -r "$_dt_home/cache/state" ] && IFS= read -r state < "$_dt_home/cache/state"
  [ -r "$_dt_home/cache/env.sh" ] && . "$_dt_home/cache/env.sh"
  [ -r "$_dt_home/cache/env.sh" ] && [ "$state" = "${DT_STATE-}" ] && return
  "$_dt_home/devtools" shell env "$_dt_shell" >/dev/null && . "$_dt_home/cache/env.sh"
}

dt() {
  "$_dt_home/devtools" "$@"
  local result=$? state=
  [ -r "$_dt_home/cache/state" ] && IFS= read -r state < "$_dt_home/cache/state"
  [ "$state" = "${DT_STATE-}" ] || _dt_reload
  return $result
}

_dt_reload

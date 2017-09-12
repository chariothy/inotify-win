inotify-win
===========
A port of the **inotifywait** tool for Windows, see https://github.com/rvoicilas/inotify-tools

Compiling
=========
If you have Cygwin installed, just run `make` in this directory. This will create the executable, `inotifywait.exe`.

Manual complilation goes as follows:

```sh
$ %WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe /t:exe /out:inotifywait.exe src\*.cs
Microsoft (R) Visual C# 2010 Compiler Version 4.0.30319.1
Copyright (C) Microsoft Corporation. Alle Rechte vorbehalten.

$ 
```

Usage
=====
The command line arguments are similar to the original one's except --timeout and --exec:

```sh
$ inotifywait.exe
Usage: inotifywait [options] path [...]

Options:
-r/--recursive:  Recursively watch all files and subdirectories inside path
-m/--monitor:    Keep running until killed (e.g. via Ctrl+C)
-q/--quiet:      Do not output information about actions
-e/--event list: Events (create, modify, delete, move) to watch, comma-separated. Default: all
--format format: Format string for output.
--exclude:       Do not process any events whose filename matches the specified regex
--excludei:      Ditto, case-insensitive

--timeout:       Timeout in seconds to run command. Default: 10
--command:       Command or program to run (cmd.exe for built-in command) (optional)
--param:         Parameter of command or program (optional)

Formats:
%e             : Event name
%f             : File name
%w             : Path name
%T             : Current date and time
```

The first public version of OpenWards, a reimplementation of Theme Hospital in C# / .NET.

**You need the original game's data files** (GOG, EA app or CD). OpenWards looks for them in the usual install
folders and, if it cannot find them, asks you to choose the folder that contains `DATA`, `LEVELS` and `QDATA`.

## Installing

1. Download `OpenWards-0.1.0-win-x64.zip` below and unpack it into any folder.
2. Run `OpenWards.exe`. Nothing else needs to be installed.

Settings, saves and `crash.log` are in `%AppData%\OpenWards`.

## What works

- The whole campaign and the original levels, with the original game's rules: rooms, staff, diseases, research,
  emergencies, epidemics, VIPs, earthquakes, heating, loans and land.
- Saving, loading and monthly autosaves; importing saved games of the original Theme Hospital.
- The original interface, movies, sounds and music; 22 languages, with a Russian announcer.
- Checking for new versions on GitHub (can be turned off in the settings).

## Known issues

- Importing an original saved game does not carry over VIP visit scheduling or the research progress of
  individual machines and drugs.
- CorsixTH saved games cannot be loaded.
- Network play is experimental.

Please report problems at https://github.com/Maxwellwr/OpenWards/issues, with `crash.log` if the game crashed.

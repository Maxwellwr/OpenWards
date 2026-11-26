OpenWards 0.2, a reimplementation of Theme Hospital in C# / .NET.

**You need the original game's data files** (GOG, EA app or CD). OpenWards looks for them in the usual install
folders and, if it cannot find them, asks you to choose the folder that contains `DATA`, `LEVELS` and `QDATA`.

## Installing

1. Download `OpenWards-0.2.0-win-x64.zip` below and unpack it into any folder (over 0.1.0 is fine).
2. Run `OpenWards.exe`. Nothing else needs to be installed.

Settings, saves and `crash.log` are in `%AppData%\OpenWards`. Saved games of 0.1.0 load.

## What's new

- The game's rules now follow a community specification of the original: reputation, prices and insurance,
  diagnosis and treatment, patients' needs, research and machine wear, VIPs, rats and loans, staff wages, training
  and rest, epidemics and emergencies, the level rating and the yearly bonus.
- Computer opponents follow their scenario, as in the original.
- Network play has land auctions and network missions, and computer opponents fill the places no person holds.
- Much more of an original saved game comes over: research, reputation, patients, staff, VIPs, emergencies,
  earthquakes, litter, rats and the computer opponents.

The full list is in `CHANGELOG.md`.

## Known issues

- Importing an original saved game does not carry over the candidates for hire, and emergencies offered but not
  answered yet are dropped. Plants' water is an estimate.
- CorsixTH saved games cannot be loaded.
- Network play is experimental. The map is not chosen by the number of players, and a player who leaves does not
  lose their hospital: a computer opponent takes the place until someone joins.

Please report problems at https://github.com/Maxwellwr/OpenWards/issues, with `crash.log` if the game crashed.

# Changelog

## 0.2.0 (unreleased)

Game logic follows a community specification of the original Theme Hospital, and much more of an original saved game
is imported. Saved games of 0.1.0 load. See [docs/release-notes.md](docs/release-notes.md) for the known issues.

### Game logic, as in the original

- Reputation is the average outcome of the patients who leave; each disease keeps its own record (#518).
- Prices, the patient's purse and insurance (#519).
- Diagnosis and treatment; treatments that are not drugs always work fully (#520).
- Patients' needs, care and leaving the hospital; health runs from 0 to 1000 (#521).
- Research and machine wear: research points by category, machines wearing out and blowing up (#522, #542); the
  research award counts the points actually distributed (#542).
- Fire extinguishers in a room lower the chance of a worn machine blowing up by 10 % each, up to four (#529).
- VIPs: the visit timer, a score for what the VIP saw and the prize money (#523, #535).
- Rats, the hospital's value and the bank loan (#524).
- Staff: promotion, wages, asking for a raise, and doctors learning in the training room (#525, #528); tiredness and
  rest in the staff room (#532). Rested staff look for any room of the kind they work in, not the room they left
  (#546); an empty room calls staff only once a patient needs it (#551); staff do not ask for a raise while resting
  (#549).
- Epidemics and emergencies, with infectiousness by two thresholds (#526, #535).
- Level rating, the yearly bonus and the winning letter (#527).
- Computer opponents follow their scenario and treat their patients in a text simulation; they hire, build and take
  loans as in the original (#533, #539).
- Network play: land auctions (#534, #548, a frame every 55 ms), network missions and which plots border which (#538,
  #543). An auction needs more money than the price, a mission just given says its full months to do, and each
  player's land has its own colour on the town map (#555).
- The pharmacy has every drug from the start; research only makes drugs better (#541).
- Plants droop by the water they have left (#544); the plant adviser judges the hospital's plant care score, as in
  the original (#556).
- A room's warmth comes from its radiators over its floor area; the heating level is not a multiplier (#550).
- Swing doors close two ticks a frame, and a person crosses a door at the original's pace (#535, #547).

### Network games

- Computer opponents hold the places no person holds and give them up when someone joins; `--no-computers` turns
  them off. The original's network game has no computer opponents: this is OpenWards' own (#557).
- Other players go by the names they joined under, not by the computer rivals' names (#555).

### Importing original saved games

- Three fields are read at their real width (#536).
- Research, reputation records, insurance debts, prices, patients' money, insurer and needs, staff discontent and
  raise requests, the year's counters, litter, rat holes and room queues (#537, #543, #545).
- VIPs, emergencies, the next earthquake, plant water, patients' route through the hospital and the year's research
  points (#540); an emergency, an earthquake or a raise request in progress (#549).
- Computer opponents go on with their scenario from the save (#533, #539).
- The plant care score (#556).

### Fixes

- Save format versions kept apart between parallel changes (#530); six Content tests and the level 8 research room
  test mended (#541, #552); a stale note on the Russian research screen dropped (#554).

## 0.1.0 (2026-11-23)

The first public version. See the [0.1.0 release](https://github.com/Maxwellwr/OpenWards/releases/tag/v0.1.0) for
what it contains and its known issues.

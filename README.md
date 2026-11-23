# CRC Companion

A Windows app that runs next to VATSIM's CRC and draws ERAM views in their own movable windows. That lets them sit outside CRC's radar display, for example in the side areas of a 43" 4K R-side layout, while looking like CRC's own views.

**[Read the user manual](docs/MANUAL.md)** for every view, menu and setting.

## Download

Get **CRCCompanion.exe** from the [latest release](../../releases/tag/latest). It's a single file with nothing to install. A new build is published automatically on every push to `main`.

## What's in this version

| Item | Status |
|---|---|
| VIEWS selector, a copy of CRC's ERAM VIEWS menu | ✅ Every button works |
| Altimeter Settings view | ✅ |
| Weather Station Report view | ✅ |
| Per-facility profiles | ✅ |
| INBND (ERAM sublists), IN SECTOR, DEPT (ERAM sublists) | ✅ Live from VATSIM data |
| The rest of the VIEWS grid | ✅ See "Grid views" below |
| CPDLC ADV, HIST, MSGOUT, TOC SET | ✅ Live from the vUSAlink hub, see "CPDLC views" below |

## Grid views

Each opens from its VIEWS button and has the usual M menu (O/T, BORDER, LINES, FONT, BRIGHT) plus the rows listed. Typed entries use the same entry box as STATIONS: `A B -C` adds A and B and removes C.

| View | Shows | M menu extras |
|---|---|---|
| AUTO HO INHIB | Flights you list, with type, altitude and IN/OUT/OFF (in your ARTCC, outside, offline). Click to delete. | ENTRY |
| CFR | Continuous Flight Plan Readout: up to ten flight plans, kept up to date. See below. | ENTRIES, RTE, FILTERS |
| CODE | Beacon Code View: codes of airborne targets in your ARTCC plus codes added with QB (shown with a period), in columns. Click a code to see who's squawking it. | COL, SORT MAN, QB |
| CONFLCT ALERT | Pairs losing separation now (`NOW`) or within 4 minutes (`E2` = in 2 min): lateral NM and vertical hundreds of feet. Vertical rate comes from recent polls. Pairs both close to the same airport are skipped. | NM (lateral minimum, default 5) |
| CRR | Continuous range readout groups: each aircraft's distance (NM) to the group's location. Entry works like CRC's LF: `BOS`, `BOS090010 E10`, `//4220N/7110W NORTH AAL1`, `BOS AAL1/DAL2` (toggles), `-E10`. Middle-click a group label to delete its aircraft or the group. | LF |
| FLIGHT EVENT | DEP, ARR, ENTR, EXIT, AMND (altitude/route), CODE changes, LGON and LGOF for your ARTCC, newest first | CLEAR |
| GROUP SUP | Code groups to suppress from CODE and UA: `1200`, `12` (1200-1277), `4000-4077`. Shows how many targets match. | ENTRY |
| HOLD LIST | Flights detected holding (a full turn within 10 NM over 8 minutes), marked `H`, plus holds you enter as `ACID [FIX] [EFC]`. Shows altitude, fix (nearest fix if detected), EFC and minutes holding. | AUTO, ENTRY |
| MRP LIST | ERAM's Meter Reference Point view, fed by VATFLOW's FCA programs for your ARTCC. See "MRP (VATFLOW FCAs)" below. | RESEQ, SPEED, DELETE, COLOR, sublists |
| SAA FILTER | Special activity airspaces you list. Click to switch ON/OFF, middle-click to delete. | ENTRY, ALL ON, ALL OFF |
| UA | Update Area: GI notes from the vUSAlink EDST, and VATFLOW Airport TMU and FCA changes for your ARTCC. See below. | MSG WAIT, TEXT |

Fixes and FRDs are read from CRC's own `%LOCALAPPDATA%\CRC\NavData.dat`. Without it, only airports resolve. Bearings are true, not magnetic.

## RECORD (macros)

ERAM's RECORD toolbar, under the VIEWS selector. See [the manual](docs/MANUAL.md#6-record-macros) for details.
- **MSG #** records up to six commands, then a label of up to seven characters (default: the first six characters). The new button waits in the MSG slot until you move it by its tear-off bar. **MSG MAX** at 99.
- **MACRO EDIT** edits a macro's commands (up to 25 lines) and label. **MACRO DELETE** deletes: left-click to mark with an X, then middle-click to delete. **CLR ALL MACROS** needs two clicks (burnt orange in between).
- **Using one:** a one-command macro is typed into CRC's MCA, and you click the track in CRC to add the flight ID. A macro with several commands waits for one flight, picked by a QF in CRC (needs vUSALink App or Lite) or a middle-click in INBND, DEPT or IN SECTOR. Each command is then typed with the flight ID.
- **Typing into CRC:** uses the vUSALink app's CRC echo method: SendInput into CRC's ERAM window only, once it's in front.
- **Buttons:** saved macros are cyan with a white Verdana Bold label at the top, as on ERAM's display.

## COMMAND MENUS (RNG BRG)

ERAM's Command Menus button, under RECORD. See [the manual](docs/MANUAL.md#7-command-menus-range-bearing-fix-and-time).
- **RNG BRG** lists LA, LB, LC, LD and LE. Picking LA, LB or LC types the code into CRC's MCA, and you finish the entry in CRC.
- LD and LE are dimmed: CRC doesn't run them.

## DRAW (annotations)

ERAM's Draw toolbar, under COMMAND MENUS. See [the manual](docs/MANUAL.md#8-draw-annotations).
- Rectangles, circles, lines (up to 29 segments) and text (20 characters by 5 lines, A FONT 1-8), up to 200 annotations, drawn over CRC.
- Four colors with a shared brightness (COLOR, default 60). Move, resize, recolor and delete through each annotation's D handle; DEL ALL needs two clicks.
- Display-anchored only: CRC doesn't share its map position, so MAP anchoring and O SIZE aren't available.

## CFR (Continuous Flight Plan Readout)

Laid out like ERAM's CFR view (manual chapter 8): title bar with DEL ALL and SHOW FIELDS, then FLID TYP BCN SPD FIX TIM ALT RTE/RMK with a rule under each flight plan. The readouts update from VATSIM every poll.

- **Add:** with CFR open, QF the aircraft in CRC (type `QF UAL123`, or `QF ` and click the track) and it's added within a few seconds; this needs vUSALink App or Lite, which reads CRC's QF readouts. Or middle-click a flight in INBND, DEPT or IN SECTOR (CFR opens if hidden). Or left-click the view and type `QF R UAL123` (or just `UAL123`; several at once; `-UAL123` removes). A CRC CID works too (`QF R 151`) once the vUSALink app has seen that aircraft's QF. A new readout's FLID is green for 30 seconds. Past ENTRIES, the oldest drops off.
- **Delete:** middle-click a FLID, then DELETE. DEL ALL clears the view.
- **Route:** ERAM dotted format (`SFO..SLC.J154.OCS..ORD`), cut at RTE characters. A boxed `*` shows the rest of the route, a boxed `R` the remarks.
- **FIX / TIM:** on the ground or prefiled, the departure airport and proposed time (`P1530`). Airborne, the next fix ahead on the route (the first one inside your facility while still outside it) with an estimate from groundspeed (`E1615`).
- **Coral A:** the ICAO equipment lists no ADS-B surveillance code (B1/B2/U1/U2/V1/V2).
- **Menu:** ENTRIES 1-10, RTE 30-100 (left-click decreases, middle-click increases), FILTERS to show or hide ALT BCN CID FIX LBL RTE SPD TIM TYP. SHOW FIELDS shows hidden columns and the full route until clicked again; it's grayed out when nothing is hidden.
- **CID:** the CRC CID and owning sector (`151 SKW5646(32)`) come from the vUSALink EDST app, which learns them from CRC QF readouts (see [CIDs from the vUSALink EDST app](#cids-from-the-vusalink-edst-app)). Without the app, or before a QF, the FLID is just the callsign.
- **Not available:** readouts can't be sent to the RA view, since there isn't one.

## UA (Update Area)

Laid out like ERAM's Update Area view (manual 6.3): a Message Waiting Area and a text area below it.

- **MESSAGE WAITING** turns yellow while a message needs acknowledging. Click it to acknowledge the oldest; it moves to the top of the text area. A scroll bar appears when the text is longer than the area (mouse wheel works too).
- **Middle-click** a message in the text area to delete it.
- **Menu:** O, BORDER, FONT, BRIGHT, MSG WAIT and TEXT (show or suppress each area, gray = shown).
- Acknowledged messages are remembered in your profile, so the same message doesn't come back after a restart.

In ERAM the UA carries GI messages from other positions and UTMs from the automation. On VATSIM there's no ERAM automation behind CRC, so these sources stand in:

| Tag | From | What |
|---|---|---|
| `GI` | vUSAlink EDST (GI button), through the vUSAlink hub | General Information notes from your ARTCC and its tier-1 neighbor centers, e.g. `1815 GI ZNY NY_CTR JFK 22L/22R`. Notes already on the hub when the companion starts go straight into the text area, as the EDST does. |
| `TMU` | VATFLOW Airport TMU (the TBFM page), through vatflow-hub | Ground stops, restrictions and arrival rate programs (AAR, MIT, gate MITs) that name one of your ARTCC's airports or have your ARTCC as requesting or providing facility, e.g. `TMU ARR RATE KDCA AAR 32 CAPPS 20MIT`. A change is sent as `AMEND`, one that ends as `CANCEL`. |
| `FLOW` | VATFLOW FCA programs (the same Supabase table as MRP) | An FCA for your ARTCC turned on, amended, or turned off (`CANCEL`). |

The vUSAlink hub is polled every 10 s, only while the UA view (or a CPDLC view) is open. For TMU the companion listens on vatflow-hub's WebSocket, which sends the whole Airport TMU state on connect and after every change; it never sends anything (only signed-in TMU editors can change it) and connects only while the UA view is open. `CRCCOMPANION_VATFLOW_HUB_URL` points it at another vatflow-hub. If a source isn't answering, its state shows in yellow at the top of the text area (`HUB DOWN`, `VATFLOW TMU DOWN`, `VATFLOW DOWN`). GI notes come from the hub's view-only monitor (the request asks for them with a GI cursor); a hub without that support shows `NO GI FROM HUB`.

## CIDs from the vUSALink EDST app

[vUSALink App and vUSALink-Lite](https://github.com/djbrombizzle/vUSALink-App) learn CRC CIDs by reading CRC's QF readouts (`151 SKW5646(32) E75L/L`) and save them to `%LOCALAPPDATA%\vUSALinkApp\cids.json`. The companion reads that file (read-only, when it changes) and shows the CID and owning sector in CFR's FLID and in the CID column of CPDLC HIST and MSGOUT. The app drops a pair when the pilot reconnects or leaves VATSIM, so the companion never shows a stale CID for long. Without the app, these stay blank as before.

## MRP (VATFLOW FCAs)

The MRP button opens ERAM's Meter Reference Point view (TI 6110.100 figures 4-220 to 4-224), built from [VATFLOW](https://vatflow.io)'s FCA (flow constrained area) programs.

**Which programs:** every FCA whose ARTCC is your facility's and that is enabled in VATFLOW (working ZDC shows only ZDC's active FCAs), as soon as the view opens. Programs enabled or created in VATFLOW appear on the next sync, and disabled ones drop off. M → DELETE, then click a sublist name in the menu, removes a program you don't need; the profile remembers it. M → ACTIVE (a companion extra, on by default) turned off also shows programs that are disabled in VATFLOW. M → RESTORE brings back deleted programs and clears suppressions and re-sequences.

**Sync with VATFLOW:** programs are read from VATFLOW's Supabase table `fcas` with the public read key VATFLOW's own pages use (`shared/fca-store.js`), filtered server-side to your ARTCC (`data->>artcc=eq.ZDC`). VATFLOW pages get realtime pushes; the companion polls every 20 seconds while the view is open. It reads each program's line, MIT or rate, altitude band, destination/origin/fix filters, ARTCC scope, exclusions, manual order and issued releases (EDCTs). Nothing is written back.

**Sequencing** is a port of VATFLOW's `computeSequence` (`shared/fca-metering.js`) with its route engine (`shared/route-engine.js`) and the same route data (vatflow.io/data/nav: FAA NASR fixes, CIFP airways and SID/STAR transitions, preferred routes; about 5 MB, cached in `cacheatflow-nav` and refreshed weekly):

- A flight is in a program when it passes the filters and its route still ahead crosses the FCA line within 2,500 NM.
- ETA: distance along the route at groundspeed; departures still climbing and ground departures use VATFLOW's climb profile (ground: wheels-up after 3 minutes or the filed time).
- Schedule: airborne flights at their ETA (too close behind the one ahead = conflict); issued releases pinned at their CTA; other ground departures slotted around them; or the program's manual order chained by separation.
- Checked against VATFLOW's own JavaScript on the same VATSIM snapshot: the same flights in all 17 ZDC programs, airborne ETAs identical. Winds aloft aren't applied (VATFLOW's still-air case).

**The view:** for each program a header (destination and restriction, `DCA MIT 35`) and a sublist titled with its metering fix in the sublist's color, then one line per flight, latest at the top: indicator (`H` = holding), AID, meter time (scheduled crossing, HHMM) and DCT, the delay to absorb (the scheduled delay, or for an airborne conflict the time it's short).

| Action | How |
|---|---|
| Re-sequence (up to 5, one program) | Left-click the AIDs in the order they should cross, middle-click the last one. Local to the companion. M → RESEQ lights while picking; click it to cancel. |
| Suppress an entry | Middle-click its AID (nothing picked), then SUPPRESS |
| Sublist color | M → click a color bar, then a sublist name. COLOR sets the colors' brightness. Default: the VATFLOW color's nearest ERAM color. |
| DCT format | M → the MIN / SEC cell beside a sublist: rounded minutes, or rounded 10 s as m:ss |
| Speed column | M → SPEED (GIM-S speed advisories aren't available, so it stays empty, as ERAM does with no data) |

## CPDLC views

The four CPDLC buttons show the datalink traffic for your ARTCC from [vUSAlink](https://github.com/djbrombizzle/vUSAlink)'s shared KUSA hub. They read the hub's view-only monitor feed every 10 seconds, only while a CPDLC view is open. The request carries only the airspace (e.g. `KZDC`), not your CID, so it never competes with your vUSAlink EDST session (the hub allows one session per CID). These views don't send anything: uplinks still go out from the vUSAlink EDST.

| View | Shows | M menu extras |
|---|---|---|
| CPDLC ADV | Things to act on: `LOGON` requests not yet accepted, `REQ` pilot requests not yet answered, `UNABLE` replies, and `LATE` uplinks with no reply after 2 minutes (last 30 minutes). Click one to clear it. | TEXT, CLEAR ALL, RESTORE |
| CPDLC HIST | ERAM's Message History view (title `HISTORY`, DEL ALL, FLID filter, columns CID ACID MSG CONTENT STAT TIME ORG): closed messages only, newest first. Click the FLID box and type a callsign, Enter to filter (an empty Enter clears it). A down arrow between TIME and ORG marks a pilot-initiated downlink. Click a message for its full text, Due To Reason and DELETE. DEL ALL clears the list. | TEXT, RESTORE |
| CPDLC MSGOUT | ERAM's Message Out view (title `MESSAGE OUT`, CLEANUP). **SPECIAL AREA**: errors in orange, UNA (pilot said UNABLE) and TIM (no reply 120 s after sending), until acknowledged: click the status for ACKNOWLEDGE (moves it to the normal area) or ACKNOWLEDGE+DELETE. **NORMAL AREA**: SNT (waiting), STBY, and closed messages dimmed (WIL, ROG, RCVD, NRR, CLSD) until CLEANUP. Click ACID, STAT or TIME to sort (filled arrow = active sort), MSG FILTER... to hide statuses. Click a message to see its full text. Last 30 minutes. | TEXT, RESTORE |
| CPDLC TOC SET | Hub link status and the aircraft logged on to CPDLC in your ARTCC, with the frequency each is tuned to now and its last CONTACT/MONITOR uplink and reply | TEXT |

MSG CONTENT uses ERAM short forms where the wording allows: `↑380` (climb), `↓090` (descend), `350` (maintain), `DRCT HAR`. Longer text is cut with `*`; click it for the full message. CID comes from the vUSALink EDST app once it has seen a QF for that aircraft; ORG stays blank (see below). `CRCCOMPANION_HUB_URL` points the companion at a different hub, e.g. a local test hub.

HISTORY STAT codes come from what the hub reports:

| STAT | When |
|---|---|
| WIL, ROG, UNA, RCVD | Uplink answered WILCO, ROGER (or ACCEPT), UNABLE, or AFFIRM/NEGATIVE |
| NRR | Uplink that expects no answer (hub notices such as ATC COMMS ESTABLISHED, CPDLC NOT IN USE, UNABLE. REVERT TO VOICE) |
| CLSD | Uplink or pilot request still open when the session ended (END SERVICE, ATC COMMS TERMINATED) |
| APRV, UNA, ROG | Pilot request (or logon request) answered with an uplink, an UNABLE, or a ROGER |
| `*` after UNA / CLSD | Has a Due To Reason (UNABLE text, or SESSION TERMINATED) |

Not shown, because vUSAlink doesn't report them: ERR, FAIL, NS, CNCL, ICMSM, ACK and the `-ERR` variants, and the ORG column (the hub logs only the ARTCC, not the sector). CID is filled from the vUSALink EDST app when it knows it.

The monitor feed has no message reference numbers, so a reply is matched to the oldest unanswered uplink to that aircraft. The hub keeps only its last 60 messages per ARTCC; the companion keeps up to 500 while it runs.

In CRC, turn off its own ALTIM SET and WX REPORT views so they aren't shown twice.

## Using it

| Action | How |
|---|---|
| Show or hide a view | Click **ALTIM SET** or **WX REPORT** in the VIEWS selector, or click a view's **-** |
| Open or close the selector grid | Click the **VIEWS** button |
| Move the selector | Click the olive bar on the VIEWS button, move the mouse, click again to place it |
| Move a view or menu | Click its title, move the mouse, click again to place it. Esc or a right click cancels. |
| View menu | Click **M** |
| Change a number (LINES, COL, FONT, BRIGHT) | Middle click raises it, left click lowers it, as in CRC. The mouse wheel works too. |
| Add or remove stations | **M → STATIONS**, then type `IAD DCA BWI RIC` and press Enter. `-RIC` removes. `KIAD` and `IAD` both work. Esc closes. |
| Delete one station | Click its line, then click DELETE |
| Scroll the WX view | Arrows, the track, or the mouse wheel |

Clicking a view never takes the keyboard away from CRC. The station entry box takes the keyboard only while it's open, then hands it back to CRC.

### Display rules (same as ERAM)

| Field | Rule |
|---|---|
| Altimeter | First digit and decimal point removed: 29.92 → `992`, 30.12 → `012`. Underlined when below 29.92. |
| Observation time | Underlined when the report is more than 65 minutes old |
| `-M-` | Shown when the report is missing or more than 120 minutes old |
| WX `>` | The report continues below the visible lines |

## Profiles

Right-click the tray icon to switch, create, duplicate, rename or delete profiles. A profile saves everything: station lists, view positions, which views are shown, every menu setting, the VIEWS, RECORD, COMMAND MENUS and DRAW button positions, macros and DRAW annotations. Changes save automatically.

Profiles are stored in `%APPDATA%\CRCCompanion\profiles\NAME.json`. The tray menu's **Open profiles folder** opens it, so profiles can be backed up or shared.

The tray menu also has **Size**, an extra scale on top of Windows display scaling so the views can match CRC's size exactly, and **Reset view positions**.

## Strip views (INBND LIST, IN SECTOR, DEPT LIST)

Set your **VATSIM CID** from the tray menu. While you're logged on, the app finds your position in VATSIM's live data, works out the ARTCC from the callsign (`DC_`/`WAS_` → ZDC), and can switch to that facility's profile automatically. When you're not connected, a profile named after a facility (e.g. `ZDC`) uses that facility.

| View | Shows |
|---|---|
| INBND | Flights outside the ARTCC projected to enter within 20 minutes (M → MIN), in sublists |
| IN SECTOR | Airborne flights inside the ARTCC. Flights working another local controller are hidden unless M → OTHERS is on. |
| DEPT | Flights on the ground at an ARTCC airport with a flight plan, plus prefiles (M → PREFILE), in sublists |

### INBND and DEPT (ERAM format)

Drawn like ERAM's Inbound and Departure List views (R-Position User Manual 13.3-13.4). Each sublist is a tear-off marker and a title, then one line per flight: the AID and its filed altitude (`60` = 6,000 ft, `150`, `170B210` block, `VFR` or `VFR/45`, `OTP`). INBND also shows the boundary time. A coral box around the altitude marks a non-RVSM aircraft, read from the flight plan's equipment (ICAO without `W`, or FAA suffix other than H/W/Z/L).

| M menu | Does |
|---|---|
| O, BORDER, TEAROFF, LINES, FONT, BRIGHT | As in ERAM. Each sublist heading always has its gold marker. |
| SORT | BOUNDARY TIME (INBND: sublists by earliest boundary time), FIX NAME (alphabetical); off = the order you entered them. Turn DELETE on, then click a sublist name to delete it. |
| Blank row under SORT | Click it, type a sublist name (fix or airport), Enter to add it |
| MIN (INBND), PREFILE (DEPT) | Companion extras: look-ahead minutes; show prefiles |

With no sublists entered, they're automatic: INBND by inbound fix (the first fix in the filed route inside your ARTCC, else the destination), DEPT by departure airport. Once you enter sublists, only those show (even when empty). INBND matches a flight by destination airport, a name in its route, or its inbound fix; DEPT by departure airport. Flights matching none are left out, as with ERAM's adapted lists.

### IN SECTOR

Each strip is one line: `mark callsign type time/destination altitude beacon route`. The route width is set by M → ROUTE. The mark shows whose frequency the aircraft is tuned to (from VATSIM's voice data):

| Mark | Meaning |
|---|---|
| `*` | Your frequency |
| `A` / `T` / `G` | A local approach, tower or ground/delivery frequency |
| `C` | Another Center sector in your ARTCC |
| `?` | In your airspace on nobody's frequency (only while you're connected) |
| `P` | Prefiled, not connected yet |

ARTCC boundaries, callsign prefixes and airports come from VATSIM's `vatspy-data-project`, refreshed weekly. Traffic refreshes every 15 seconds.

## Weather data

By default the app shows **real-world METARs** from aviationweather.gov, fetched for all stations in one request. VATSIM's METAR service (`metar.vatsim.net`) fills in any station aviationweather.gov doesn't return. Use the tray menu's **Weather source** to make VATSIM the primary source instead.

Reports refresh every 2 minutes. A newly added station, or a change of source, is fetched right away. If a refresh fails, the last good report stays on screen.

## Building

The project is .NET 8 WPF. GitHub Actions (`.github/workflows/build.yml`) builds it on Windows and publishes the .exe. The platform-neutral logic in `src/CrcCompanion/Core` has a dependency-free test runner:

```
dotnet run --project tests/CoreTests
```

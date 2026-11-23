# CRC Companion User Manual

CRC Companion is a Windows app that runs next to VATSIM's CRC and draws ERAM views in their own windows. You can place them anywhere on your screens, for example in the side areas of a large R-side layout, and they look and behave like CRC's own ERAM views. The views are built from VATSIM's live data and from the vUSAlink and VATFLOW services.

This manual covers every view and menu. For a short overview, see the [README](../README.md).

## Contents

1. [Getting started](#1-getting-started)
2. [The basics: windows, menus and the mouse](#2-the-basics-windows-menus-and-the-mouse)
3. [The tray menu](#3-the-tray-menu)
4. [Profiles and your facility](#4-profiles-and-your-facility)
5. [The VIEWS selector](#5-the-views-selector)
6. [RECORD: macros](#6-record-macros)
7. [Weather views: ALTIM SET and WX REPORT](#7-weather-views-altim-set-and-wx-report)
8. [STATUS](#8-status)
9. [Strip views: INBND, IN SECTOR and DEPT](#9-strip-views-inbnd-in-sector-and-dept)
10. [CFR: Continuous Flight Plan Readout](#10-cfr-continuous-flight-plan-readout)
11. [UA: Update Area](#11-ua-update-area)
12. [MRP LIST: Meter Reference Point](#12-mrp-list-meter-reference-point)
13. [CPDLC views](#13-cpdlc-views)
14. [The other grid views](#14-the-other-grid-views)
15. [Working with the vUSALink EDST app](#15-working-with-the-vusalink-edst-app)
16. [Data sources and privacy](#16-data-sources-and-privacy)
17. [Troubleshooting](#17-troubleshooting)
18. [Reference](#18-reference)

---

## 1. Getting started

### What you need

- Windows 10 or 11 (64-bit).
- CRC, if you want fixes and FRDs to resolve. The companion reads CRC's navigation data file. It doesn't need CRC to be running.
- An internet connection for VATSIM data, weather, vUSAlink and VATFLOW.

### Install

1. Download **CRCCompanion.exe** from the [latest release](https://github.com/djbrombizzle/CRC-Companion/releases/tag/latest).
2. Put it anywhere you like, for example `Documents\CRC Companion`.
3. Run it.

It's a single self-contained file: there's nothing to install and no .NET to download. Windows SmartScreen may warn about an unsigned app the first time. Click **More info → Run anyway**.

A new build is published automatically every time the project changes, under the same link. To update, download it again and replace the old file. Your settings and profiles are kept, because they're stored separately (see [Files and folders](#files-and-folders)).

### First run

1. The app starts with the **VIEWS** button on screen and a tray icon near the clock.
2. Right-click the tray icon and choose **Set VATSIM CID...**. Enter your VATSIM CID. The app uses it only to find your controller position in VATSIM's public data, so it knows which ARTCC you're working.
3. Click **VIEWS** to open the selector grid. Each button opens or closes a view.
4. Move each view where you want it: click its title bar, move the mouse, and click again to drop it.
5. In CRC, turn off any views you now show in the companion instead (for example CRC's own ALTIM SET and WX REPORT), so they aren't shown twice.

Everything you change saves automatically to the current profile.

---

## 2. The basics: windows, menus and the mouse

The views copy CRC's ERAM look and mouse conventions.

### Moving things

| To | Do this |
|---|---|
| Move a view or a menu | Click its title, move the mouse, click again to place it. Esc or a right-click cancels. |
| Move the VIEWS button | Click the olive bar on the VIEWS button, move, click again. |
| Hide a view | Click the **-** at the right of its title bar, or click its button in the VIEWS selector. |
| Put every view back on screen | Tray menu → **Reset view positions**. |

### The M menu

Every view has an **M** at the left of its title bar. Click it to open the view's menu. Most menus start with the same rows:

| Row | What it does |
|---|---|
| **O / T** | Opaque or transparent background. The row shows the mode a click switches *to*: `T` while the view is opaque, `O` while it's see-through. |
| **BORDER** | Show or hide the view's outline. |
| **TEAROFF** | Strip and list views: show or hide tear-off markers. |
| **LINES** | How many lines the view shows before it scrolls. |
| **FONT** | Text size, 1 to 5. |
| **BRIGHT** | Text brightness, 0 to 100. |

A gray row is on and a black row is off. Green rows hold numbers.

### Changing numbers

As in CRC, **middle-click raises** a number and **left-click lowers** it. The mouse wheel works too. This applies to LINES, COL, FONT, BRIGHT, ROUTE, MIN, ENTRIES, RTE, NM and the other number rows.

### Typing

- Some menu rows (STATIONS, ENTRY, QB, LF and others) open a one-line **entry box** under the view. Type the entry and press **Enter**. **Esc** closes the box.
- Entries use one pattern everywhere: `A B C` adds items, and `-C` removes one. For example, `IAD DCA -RIC`.
- Clicking a view never takes the keyboard away from CRC. Only an open entry box, or a field you clicked to type in, takes the keyboard. It hands the keyboard back to CRC when you press Enter or Esc, or click elsewhere.

### Pop-ups

Clicking an item often opens a small pop-up menu at the mouse, for example DELETE or SUPPRESS. Click elsewhere to close it.

---

## 3. The tray menu

Right-click the companion's tray icon (near the clock):

| Item | What it does |
|---|---|
| *(status line)* | Your session: connected position and facility, or why there's no facility yet. |
| *Profile: NAME* | The profile in use. |
| **Set VATSIM CID...** | Your VATSIM CID, used to find your controller position in the VATSIM data. |
| **Switch profile when I log on** | When you log on as a controller, switch to the profile named after your facility (for example `ZDC`). |
| *(profile list)* | Click a profile to switch to it. |
| **New profile...** / **Duplicate current...** / **Rename current...** / **Delete current** | Manage profiles. |
| **Size** | An extra scale on top of Windows display scaling (75% to 250%), so the views can match CRC's size exactly. Saved with the profile. |
| **Weather source** | **Real world (aviationweather.gov)**, the default, or **VATSIM (metar.vatsim.net)**. |
| **Reset view positions** | Moves every view back to a default spot on screen. |
| **Open profiles folder** | Opens the folder holding your profiles, for backup or sharing. |
| **Exit** | Closes the companion. |

---

## 4. Profiles and your facility

### Profiles

A profile saves everything:
- which views are shown and where;
- every menu setting;
- station lists, sublists and entries;
- the VIEWS selector's position.

Changes save automatically a moment after you make them.

Profiles are JSON files in `%APPDATA%\CRCCompanion\profiles\`. You can copy one to another PC or share it, and the companion picks it up on its next start.

### How the facility is chosen

Most views need to know your ARTCC: the strip lists, MRP, UA, CPDLC and others. The companion works it out like this:

1. **Logged on as a controller:** your CID is found in VATSIM's live data, and the ARTCC comes from your callsign (`DC_` or `WAS_` → ZDC). With **Switch profile when I log on** ticked, the profile named after that facility (`ZDC`) is loaded.
2. **Not logged on:** a profile named after a facility uses that facility. A profile called `ZDC` shows ZDC traffic even while you're offline, which is handy for setting up.
3. **Neither:** views that need a facility say `NO FACILITY` or ask you to set your CID.

ARTCC boundaries, callsign prefixes and airports come from VATSIM's `vatspy-data-project`, refreshed weekly. Traffic refreshes every 15 seconds.

---

## 5. The VIEWS selector

The **VIEWS** button opens a grid that copies CRC's ERAM VIEWS menu. Every button works:

| Button | Section |
|---|---|
| ALTIM SET, WX REPORT | [7](#7-weather-views-altim-set-and-wx-report) |
| STATUS | [8](#8-status) |
| INBND LIST, DEPT LIST | [9](#9-strip-views-inbnd-in-sector-and-dept) |
| CFR | [10](#10-cfr-continuous-flight-plan-readout) |
| UA | [11](#11-ua-update-area) |
| MRP LIST | [12](#12-mrp-list-meter-reference-point) |
| CPDLC ADV, CPDLC HIST, CPDLC MSGOUT, CPDLC TOC SET | [13](#13-cpdlc-views) |
| AUTO HO INHIB, CODE, CONFLCT ALERT, CRR, FLIGHT EVENT, GROUP SUP, HOLD LIST, SAA FILTER | [14](#14-the-other-grid-views) |

A lit button means its view is shown. **IN SECTOR** is a companion extra with its own button in a row under the grid.

---

## 6. RECORD: macros

The **RECORD** button sits under the VIEWS selector and works like ERAM's RECORD toolbar. A macro is up to six commands saved behind a button you place on the screen. Clicking the button enters those commands in CRC for a flight.

Click **RECORD** to open or close its toolbar:

| Button | What it does |
|---|---|
| **MSG 4** | Records a new macro. The number is the macro you're about to record. At 99 macros it reads **MSG MAX** and nothing more can be recorded. |
| **MACRO EDIT** | Lists your macros so you can change one. |
| **MACRO DELETE** | Deletes macros. |
| **CLR ALL MACROS** | Deletes every macro. It turns burnt orange; click it again to confirm. |

### Recording a macro

1. Click **MSG**. An input box opens under it and takes the keyboard.
2. Type a command without the flight ID, for example `QZ 340`. A `;` at the end is fine; it's removed.
3. Press **Enter** to start the next command, up to six. The arrow keys, Backspace and Delete edit the lines.
4. Click **SAVE MSG**. The MSG button turns into a blank button with a cursor.
5. Type a label of up to seven characters and press **Enter**. With no label, the first six characters of the commands are used.
6. The new macro button now sits where MSG was. Click its gold tear-off bar, move the mouse, and click to place it. MSG comes back once the macro has been moved, and you have to move it before you can record another one.

Press **Esc** (ERAM's CLEAR), or click anywhere outside the toolbar, to cancel recording. Nothing is saved.

### Using a macro

Click a macro button with the left or middle button. What happens next depends on how many commands it has.

**One command** (for example `QZ 240`):
1. The companion types the command into CRC's MCA, with no Enter, and leaves the keyboard in CRC. Anything already typed in the MCA is cleared first.
2. Click the aircraft's track in CRC, as you would after typing the command yourself. CRC adds the flight ID.
3. Middle-click the track, or left-click it and press Enter, to send. As in ERAM, a single-command macro can take several flights (up to 15) by clicking each track.

**Several commands** (for example `QZ 340`, `QP J`, `QP 22`):
1. The macro button lights up and waits up to 60 seconds for one flight. The line under the RECORD toolbar says what it's waiting for.
2. Pick the flight in either of these ways:
   - **Middle-click it in INBND, DEPT or IN SECTOR.** This needs nothing else running. While a macro is waiting, the middle-click goes to the macro instead of CFR.
   - **QF it in CRC**: type `QF AAL123`, or type `QF ` and click the track. This needs vUSALink App or vUSALink-Lite, which reads CRC's QF readout and tells the companion which flight it was ([section 15](#15-working-with-the-vusalink-edst-app)). A middle-click on a data block with the MCA empty isn't always a QF: on another sector's flight, CRC forces the data block instead.

   A one-command macro needs neither: CRC adds the flight ID itself when you click the track. A macro with several commands has to type the flight ID into every line, so the companion must learn the flight first.
3. Each command is typed into CRC with the flight ID and Enter, one after another.

Click the lit button again to cancel. These macros take one flight only; ERAM gives the same `TOO MANY FLIGHT IDS` error.

**How the commands get into CRC.** CRC has no command interface, so the companion types the commands the same way the vUSALink app's CRC echo does:
- It brings CRC's ERAM window to the front and sends the keystrokes.
- It types only into a CRC window whose title shows an ERAM display, never CRC's Messages or Controllers windows.
- If it can't bring that window to the front, it types nothing.

Problems show in yellow under the RECORD toolbar, for example `NOT TYPED: CRC NOT RUNNING` or `NOT TYPED: NO ERAM WINDOW`. If CRC runs as administrator, Windows blocks the keystrokes; run the companion as administrator too.

**Check before you send.** A one-command macro leaves its line in CRC's MCA so you can see it before you send. A macro with several commands sends them straight away, and the flight is named in the feedback line. With the QF method, the vUSALink app can occasionally misread a readout. If it does, it may pick up the readout already on screen instead, which means the wrong aircraft. Prefer the list middle-click when you can.

### Editing a macro

1. Click **MACRO EDIT**. Your macros are listed beside it.
2. Click one. A box opens with its commands (up to 25 lines) and its label below them. **Tab** switches between the two, or you can click either one.
3. Click **SAVE MSG**, or press Enter in the label, to save. The button's label updates.

### Deleting macros

- **One macro:** click **MACRO DELETE**, then middle-click the macro, either in the list or the button on the screen.
- **Several:** left-click each macro except the last; an X appears over each one. Then middle-click the last one. All the marked macros are deleted.
- **All of them:** click **CLR ALL MACROS** twice.

### Good to know

- **Saving:** macros and their positions are saved in the profile, which is the companion's version of an ERAM preference set. Switching profile switches macros.
- **Placement:** macro buttons are placed in screen pixels, not tied to map coordinates. The companion can't see CRC's map range.
- **Resetting positions:** **Reset view positions** moves the RECORD toolbar back under VIEWS. Macro buttons stay where you put them.
- **Overflow:** with more than 99 macros, the oldest are dropped.

---

## 7. Weather views: ALTIM SET and WX REPORT

### ALTIM SET (Altimeter Settings)

Shows the station, observation time and altimeter for each station you list, in columns.

| Menu | |
|---|---|
| LINES, COL | Rows per column and the number of columns. |
| FONT, BRIGHT | Text size and brightness. |
| TEMPLATE | Shown for fidelity. CRC doesn't simulate it either. |
| STATIONS | Opens the entry box. `IAD DCA BWI RIC` adds stations and `-RIC` removes one. `KIAD` and `IAD` both work. |

To delete one station, click its line and then **DELETE**.

### WX REPORT (Weather Station Report)

Shows the full METAR for each station you list.

| Menu | |
|---|---|
| LINES | Visible lines. A `>` at the end of the last line means the report continues below. |
| FONT, BRIGHT, STATIONS | As above. |

Scroll with the arrows, by clicking the track, or with the mouse wheel.

### Display rules (same as ERAM)

| Field | Rule |
|---|---|
| Altimeter | First digit and decimal point removed: 29.92 → `992`, 30.12 → `012`. Underlined when below 29.92. |
| Observation time | Underlined when the report is more than 65 minutes old. |
| `-M-` | The report is missing or more than 120 minutes old. |

### Where the weather comes from

- **Default:** real-world METARs from aviationweather.gov, fetched for all stations in one request. VATSIM's METAR service fills in any station aviationweather.gov doesn't return.
- **Change it:** tray menu → **Weather source** makes VATSIM the main source instead.
- **Refresh:** reports update every 2 minutes. A newly added station, or a change of source, is fetched right away. If a refresh fails, the last good report stays on screen.

---

## 8. STATUS

ERAM's STATUS view, with text you control. It starts with the lines from an FAA Academy ERAM frame: `CH: B - ACTIVE`, `CA ON`, `MCI ON`, `WEATHER` and so on.

- **Edit:** click inside the view to edit it. An underscore cursor appears, as in the MCA.
  - Type to change a line. Enter splits the line at the cursor. The arrow keys, Home, End, Backspace and Delete work as in a text editor. The view holds up to 40 lines of 40 characters.
  - Press Esc, or click elsewhere, to finish. The keyboard goes back to CRC.
- **Menu:** O/T, BORDER, FONT and BRIGHT, plus **DEFAULT**, which puts the original lines back.

---

## 9. Strip views: INBND, IN SECTOR and DEPT

These lists come from VATSIM's live data for your ARTCC and refresh every 15 seconds.

| View | Shows |
|---|---|
| **INBND** | Flights outside your ARTCC projected to enter within 20 minutes (set with M → MIN), in sublists, with the boundary time. |
| **DEPT** | Flights on the ground at one of your ARTCC's airports with a flight plan, plus prefiles (M → PREFILE), in sublists. |
| **IN SECTOR** | Airborne flights inside your ARTCC, one strip line each. |

### INBND and DEPT (ERAM sublist format)

These are drawn like ERAM's Inbound and Departure List views (R-Position User Manual 13.3–13.4).
- **Sublist heading:** each sublist starts with a gold tear-off marker and its title (an airport or fix).
- **Flight lines:** then one line per flight: the AID (callsign) and its filed altitude.
  - **Altitude formats:** `60` is 6,000 ft, and `150`, `170B210` (block), `VFR`, `VFR/45` and `OTP` are as filed.
  - **Boundary time:** INBND also shows when the flight crosses into your airspace.
- **Coral box around the altitude:** the aircraft isn't RVSM approved, read from the flight plan's equipment (ICAO equipment without `W`, or an FAA suffix other than H, W, Z or L).

| M menu | What it does |
|---|---|
| O, BORDER, TEAROFF, LINES, FONT, BRIGHT | As in ERAM. Each sublist heading always shows its gold marker. |
| **SORT** | Opens the sort pop-up. **BOUNDARY TIME** (INBND only) orders sublists by their earliest boundary time. **FIX NAME** orders them alphabetically. Neither keeps the order you entered them. **DELETE**: turn it on, then click a sublist name to delete that sublist. |
| *(blank row under SORT)* | Click it, type a sublist name (fix or airport), and press Enter to add it. |
| **MIN** (INBND) | Look-ahead time, 5 to 60 minutes. |
| **PREFILE** (DEPT) | Include prefiled flight plans. |

**Automatic and adapted sublists:**
- **No sublists entered:** the lists group themselves.
  - INBND groups by inbound fix: the first fix in the filed route that's inside your ARTCC, otherwise the destination.
  - DEPT groups by departure airport.
- **Sublists entered:** only those show, even when empty.
  - INBND puts a flight in the first sublist that matches its destination airport, a name in its route, or its inbound fix.
  - DEPT matches by departure airport.
  - A flight that matches none is left out, as with ERAM's adapted lists.

**Highlighting:** click a flight's line to highlight it, and click again to clear it.

**Send to CFR:** middle-click a flight's line (here or in IN SECTOR) to add it to the CFR view ([section 10](#10-cfr-continuous-flight-plan-readout)). CFR opens if it's hidden.

### IN SECTOR

Each strip is one line: `mark callsign type time/destination altitude beacon route`. Click a strip to highlight it; middle-click it to add the flight to CFR. M → **ROUTE** sets how much of the route shows. Flights working another local controller are hidden unless M → **OTHERS** is on.

The mark shows whose frequency the aircraft is tuned to, from VATSIM's voice data:

| Mark | Meaning |
|---|---|
| `*` | Your frequency |
| `A` / `T` / `G` | A local approach, tower or ground/delivery frequency |
| `C` | Another Center sector in your ARTCC |
| `?` | In your airspace on nobody's frequency (only while you're connected) |
| `P` | Prefiled, not connected yet |

---

## 10. CFR: Continuous Flight Plan Readout

ERAM's Continuous Flight Plan Readout view (ERAM manual chapter 8). It keeps up to ten flight plans on screen, kept up to date from VATSIM.

```
 M |                     CFR                      | DEL ALL | SHOW FIELDS | -
   FLID              TYP    BCN  SPD FIX TIM   ALT RTE/RMK
   151 SWA2454(32)   B738/L 7304 452 BWI E1751 350 SAN.CLSSY3.TGOLD..PXR..LAKEY..FORPE [*][R]
 A N15W              A339/L 2176 550 IAD P1330 380 IAD..KIAH.GUMBY3.LLA..LEV.Y290.GAWK [*][R]
```

### Adding and removing flight plans

| To | Do this |
|---|---|
| Add from CRC | With CFR open, QF the aircraft in CRC: **middle-click its data block**, or type `QF UAL123`. It appears in CFR within a few seconds. Needs vUSALink App or vUSALink-Lite running (see below). |
| Add from a list | **Middle-click** the flight in INBND, DEPT or IN SECTOR. CFR opens if it's hidden. |
| Add by typing | Left-click anywhere in the view. In the entry box type `QF R UAL123`, or just `UAL123`. Several at once work: `UAL123 DAL45`. |
| Remove one | Middle-click its FLID, then click **DELETE UAL123**. Or type `-UAL123` in the entry box. |
| Remove all | **DEL ALL** in the title bar. |

- **New readouts:** a new readout's FLID shows in green for 30 seconds.
- **Too many:** when you add more than ENTRIES allows, the oldest readout drops off.
- **Already listed:** adding a flight that's already in the view moves it to the end.
- **No flight plan:** a callsign with no flight plan on VATSIM is refused with `NO FP UAL123`.

**QF from CRC: how it works.**
- **ERAM behavior:** in ERAM, a QF with the CFR view open puts the readout in CFR. The companion does the same.
- **No direct link to CRC:** the companion can't see clicks on CRC's scope.
- **What the vUSALink app does:** vUSALink App and vUSALink-Lite catch each QF in CRC (a typed QF, or a middle-click on a data block), read the readout off CRC's screen, and record the aircraft in `cids.json`.
- **What the companion does:** while CFR is open, it checks that file every second and adds each aircraft QF'd since CFR was opened. The CID and owning sector come with it.
- **When CFR is hidden:** QFs aren't added.
- **Delay:** expect 1–3 seconds while the app reads CRC's screen.
- **When nothing is added:** if the app can't read a readout and match it to a real VATSIM flight, nothing is added; QF again or type the callsign. If the app can't read the new readout, it may pick up the readout already on screen instead, which only moves that flight to the end.

### Columns

| Column | Shows |
|---|---|
| **FLID** | `CID ACID(sector)`. The CID and owning sector come from the vUSALink EDST app ([section 15](#15-working-with-the-vusalink-edst-app)); without it, just the callsign. A coral **A** in front means the flight isn't ADS-B equipped: its ICAO equipment lists no ADS-B code (B1, B2, U1, U2, V1, V2). |
| **TYP** | Type and equipment suffix, `B738/L`. |
| **BCN** | Assigned beacon code. |
| **SPD** | Filed cruise speed (TAS). |
| **FIX / TIM** | The coordination fix and time (see below). |
| **ALT** | Filed altitude, ERAM format. |
| **RTE/RMK** | The route in ERAM's dotted format, cut to RTE characters. A boxed **\*** means there's more route: click it to show the rest under the entry. A boxed **R** means there are remarks: click it to show them. |

**FIX and TIM:**
- **On the ground or prefiled:** the departure airport and proposed departure time, `IAD P1330`.
- **Airborne:** the next fix ahead on the filed route, with an estimate from groundspeed, `BWI E1751`. While the flight is still outside your ARTCC, it's the first fix inside it.

**Route format:**
- `..` joins two fixes flown direct.
- A single `.` joins a fix to an airway or procedure: `SFO..SLC.J154.OCS..DBQ.JVL4.ORD`.
- `DCT` and speed/level groups are dropped.

### The CFR menu

| Row | What it does |
|---|---|
| O, BORDER, FONT, BRIGHT | As usual. |
| **ENTRIES** | How many flight plans the view holds, 1 to 10. Lowering it deletes the oldest. |
| **RTE** | Route characters shown, 30 to 100. |
| **FILTERS** | Opens the filter pop-up: ALT, BCN, CID, FIX, LBL (column labels), RTE, SPD, TIM, TYP. Green shows the column; click to hide it. Changes apply at once. |

**SHOW FIELDS** in the title bar shows every hidden column and the full route until you click it again. It's grayed out when nothing is hidden or the view is empty.

If a flight disconnects from VATSIM, its last readout stays on screen until you delete it.

---

## 11. UA: Update Area

ERAM's Update Area view (ERAM manual 6.3): a **Message Waiting Area** and a **text area** for messages you need to see and acknowledge.

```
 M |              UA               | -
            MESSAGE WAITING
   1815 GI ZNY NY_CTR JFK 22L/22R
   1802 TMU ARR RATE KDCA AAR 32 CAPPS
   20MIT CLIPR 35MIT
```

### Using it

| To | Do this |
|---|---|
| See that a message is waiting | **MESSAGE WAITING** turns yellow. It's dim gray when nothing is waiting. |
| Acknowledge | Click **MESSAGE WAITING**. The oldest waiting message moves to the top of the text area. Click again for the next one. |
| Read older messages | The text area scrolls: use the scroll bar or the mouse wheel. |
| Delete a message | Middle-click it in the text area, then **DELETE**. |

**Menu:** O, BORDER, FONT, BRIGHT, plus **MSG WAIT** and **TEXT**, which show or suppress each area (gray = shown, black = suppressed).

Acknowledged messages are remembered in your profile, so a message you've seen doesn't come back after a restart.

### Where the messages come from

In real ERAM the UA carries GI (General Information) messages sent by other positions and UTMs from the automation. On VATSIM these sources stand in:

| Tag | Source | Messages |
|---|---|---|
| **GI** | The **GI** button in the vUSAlink EDST, through the vUSAlink hub | Free-text notes from controllers in your ARTCC and its neighboring (tier-1) centers: `1815 GI ZNY NY_CTR JFK 22L/22R`. |
| **TMU** | VATFLOW's **Airport TMU** page, through vatflow-hub | Ground stops, restrictions and arrival rate programs that concern your ARTCC: `TMU GROUND STOP KIAD UNTIL 1930`, `TMU JFK VIA ALL 20MIT ZNY:ZDC 1700-2100`, `TMU ARR RATE KDCA AAR 32 CAPPS 20MIT`. |
| **FLOW** | VATFLOW **FCA** programs | An FCA for your ARTCC turned on, amended or turned off: `FLOW FCA DCA MIT 35`. |

**How the sources behave:**
- **What concerns your ARTCC:** an initiative concerns you if it names one of your ARTCC's airports, or your ARTCC is its requesting or providing facility.
- **Changes:** when a TMU initiative or FCA changes, a new message arrives starting with `AMEND`. When one ends, the message starts with `CANCEL`.
- **GI at startup:** GI notes already on the hub when the companion starts are filed straight into the text area as read, as the EDST does. Only new notes light MESSAGE WAITING.

**Sending GI:**
- Use the **GI** button in the vUSAlink EDST. The companion only reads.
- The EDST lets you send once you're signed in and verified for a position.
- Everyone in your ARTCC and its neighbor centers sees the note, in the EDST and in their companion's UA.

**Problems with a source:** if a source isn't answering, a yellow line at the top of the text area says which: `HUB DOWN`, `VATFLOW TMU DOWN` or `VATFLOW DOWN`.

---

## 12. MRP LIST: Meter Reference Point

ERAM's Meter Reference Point view (TI 6110.100 figures 4-220 to 4-224), built from [VATFLOW](https://vatflow.io)'s FCA (flow constrained area) programs.

### What you see

For each program:
- **A header:** the destination and restriction, for example `DCA MIT 35`.
- **A sublist:** titled with its metering fix, in the sublist's color.
- **One line per flight, latest at the top:**
  - **Indicator:** `H` means holding.
  - **AID:** the callsign.
  - **Meter time:** the scheduled crossing, HHMM.
  - **DCT:** the delay to absorb. That's the scheduled delay or, for an airborne conflict, the time the flight is short.

### Which programs show

- **Shown:** every FCA for your ARTCC that's enabled in VATFLOW (working ZDC, you see ZDC's active FCAs).
- **Changes:** programs enabled or created in VATFLOW appear on the next sync, about every 20 seconds while the view is open. Disabled ones drop off.
- **Removing one:** M → **DELETE**, then click a sublist name in the menu. The profile remembers it.
- **Showing disabled programs:** turn M → **ACTIVE** off (it's on by default).
- **Undoing:** M → **RESTORE** brings back deleted programs and clears suppressions and re-sequences.

### Actions

| Action | How |
|---|---|
| Re-sequence (up to 5, one program) | Left-click the AIDs in the order they should cross, then middle-click the last one. While you're picking, M → **RESEQ** is lit; click it to cancel. This is local to your companion: VATFLOW isn't changed. |
| Suppress an entry | Middle-click its AID (with nothing picked), then **SUPPRESS**. |
| Sublist color | M → click a color bar, then a sublist name. **COLOR** sets how bright the colors are. |
| DCT format | M → the **MIN / SEC** cell beside a sublist: rounded minutes, or rounded 10 s as m:ss. |
| Speed column | M → **SPEED**. GIM-S speed advisories aren't available, so it stays empty, as ERAM does with no data. |

### How the sequence is worked out

The sequencing is a port of VATFLOW's own metering code, and it uses VATFLOW's route data: FAA NASR fixes, airways, SID/STAR transitions and preferred routes.
- **Download:** that data is about 5 MB. It downloads the first time you open MRP and is refreshed weekly.
- **Matches VATFLOW:** on the same traffic it gives the same flights and ETAs as VATFLOW. Winds aloft aren't applied, which matches VATFLOW's still-air case.

| Step | Rule |
|---|---|
| Which flights | A flight is in a program when it passes the program's filters and its remaining route crosses the FCA line within 2,500 NM. |
| ETA | Distance along the route at groundspeed. Departures still climbing, and flights on the ground, use VATFLOW's climb profile; ground flights are assumed wheels-up 3 minutes from now or at the filed time. |
| Schedule | Airborne flights are scheduled at their ETA; one too close behind the one ahead is a conflict. Issued releases (EDCTs) are pinned at their CTA. Other ground departures are slotted around them. A program with a manual order is chained in that order by its separation. |

---

## 13. CPDLC views

The four CPDLC buttons show the datalink traffic for your ARTCC from [vUSAlink](https://github.com/djbrombizzle/vUSAlink)'s shared hub.

These views **only read**: uplinks are still sent from the vUSAlink EDST. The companion reads the hub's public feed every 10 seconds while a CPDLC view is open. It sends only your airspace (`KZDC`), never your CID, so it can't disturb your EDST session (the hub allows one session per CID).

**ERAM short forms:**
- **Shortening:** message text uses ERAM short forms where the wording allows: `↑380` (climb), `↓090` (descend), `350` (maintain) and `DRCT HAR` (direct).
- **Cut text:** longer text is cut off with a `*`. Click the message for the full text.
- **Width:** M → **TEXT** sets how wide the message column is.

### CPDLC ADV

Things to act on, from the last 30 minutes:

| Type | Meaning |
|---|---|
| `LOGON` | A logon request not yet accepted. |
| `REQ` | A pilot request not yet answered. |
| `UNABLE` | A pilot answered UNABLE. |
| `LATE` | An uplink with no reply after 2 minutes. |

Click one to clear it. The menu has **CLEAR ALL** and **RESTORE**.

### CPDLC HIST (Message History)

ERAM's HISTORY view: closed messages only, newest first.
- **Title bar:** `HISTORY` and **DEL ALL**.
- **FLID filter:** click the FLID box and type a callsign, then press Enter to filter. An empty Enter clears the filter.
- **Columns:** CID ACID MSG CONTENT STAT TIME ORG.
- **Down arrow:** an arrow between TIME and ORG marks a pilot-initiated downlink.
- **Message details:** click a message for its full text, its Due To Reason, and **DELETE**.
- **Clearing:** DEL ALL clears the list, and M → **RESTORE** brings it back.

| STAT | Meaning |
|---|---|
| WIL, ROG, UNA, RCVD | The uplink was answered WILCO, ROGER (or ACCEPT), UNABLE, or AFFIRM/NEGATIVE. |
| NRR | An uplink that expects no reply, such as hub notices. |
| CLSD | Still open when the session ended. |
| APRV, UNA, ROG | A pilot request answered with an uplink, UNABLE or ROGER. |
| `*` after UNA or CLSD | There's a Due To Reason. |

vUSAlink doesn't report ERR, FAIL, NS, CNCL, ICMSM or ACK, so these never appear. ORG stays blank because the hub logs only the ARTCC, not the sector.

### CPDLC MSGOUT (Message Out)

ERAM's MESSAGE OUT view, covering the last 30 minutes.

- **SPECIAL AREA:** problems that stay until you acknowledge them.
  - **Shown in orange:** UNA (the pilot said UNABLE), TIM (no reply 120 seconds after sending), and errors.
  - **Acknowledging:** click the status, then **ACKNOWLEDGE** (moves it to the normal area) or **ACKNOWLEDGE+DELETE**.
- **NORMAL AREA:** the other uplinks.
  - **Waiting:** SNT (sent, waiting for a reply) and STBY.
  - **Closed:** WIL, ROG, RCVD, NRR and CLSD show dimmed until you click **CLEANUP**.
- **Sorting:** click the **ACID**, **STAT** or **TIME** header to sort. The filled arrow marks the active sort; click again to reverse it.
- **MSG FILTER...:** hides chosen statuses.
- **Full text:** click a message to see all of it.

### CPDLC TOC SET

Shows whether the hub link is up, and lists the aircraft logged on to CPDLC in your ARTCC. For each one it gives the frequency it's tuned to now, its last CONTACT or MONITOR uplink, and the pilot's reply.

### Good to know

- **Matching replies:** the hub's feed has no message reference numbers, so a reply is matched to the oldest unanswered uplink to that aircraft.
- **How much is kept:** the hub keeps its last 60 messages per ARTCC, and the companion keeps up to 500 while it runs.
- **CID column:** filled when the vUSALink EDST app has learned the aircraft's CID ([section 15](#15-working-with-the-vusalink-edst-app)).

---

## 14. The other grid views

Each has the usual M menu (O/T, BORDER, LINES, FONT, BRIGHT) plus the rows listed. Typed entries use the entry box: `A B -C`.

### AUTO HO INHIB

Flights you've marked to inhibit automatic handoff. Each line shows type, altitude and where the flight is: **IN** your ARTCC, **OUT**side it, or **OFF**line.
- **Add:** M → **ENTRY** with `UAL123`, or `-UAL123` to remove.
- **Delete:** click a line, then **DELETE**.

### CODE (Beacon Code View)

Codes of airborne targets in your ARTCC, plus codes you add with QB, in columns.
- **QB codes** show with a period after them.
- **Who's squawking:** click a code to see which aircraft use it. QB codes also offer DELETE.
- **COL:** number of columns.
- **SORT MAN:** sort as entered.
- **QB:** type `1200 7700` to add those codes; entering a listed code again removes it.
- **Hidden codes:** codes covered by GROUP SUP aren't shown.

### CONFLCT ALERT

Pairs losing separation now (`NOW`) or soon (`E2` = in 2 minutes, looking up to 4 minutes ahead), with the lateral distance in NM and the vertical distance in hundreds of feet.
- **Vertical rates** come from recent polls.
- **Skipped:** pairs where both aircraft are close to the same airport.
- **NM:** the lateral minimum, 3 to 10 (default 5).

### CRR (Continuous Range Readout)

Groups of aircraft with each one's distance in NM to the group's location. Entry works like CRC's LF command:

| Entry | Does |
|---|---|
| `BOS` | Group at Boston. |
| `BOS090010 E10` | Group at the BOS 090° radial, 10 NM, labeled E10. |
| `//4220N/7110W NORTH AAL1` | Group at a lat/long, labeled NORTH, with AAL1 in it. |
| `BOS AAL1/DAL2` | Adds the aircraft to the BOS group, or removes them if already there. |
| `-E10` | Deletes group E10. |

Middle-click a group label to delete its aircraft or the whole group.

### FLIGHT EVENT

Events in your ARTCC, newest first:
- **DEP** and **ARR:** departures and arrivals.
- **ENTR** and **EXIT:** entering or leaving your ARTCC.
- **AMND:** an altitude or route amendment.
- **CODE:** a beacon code change.
- **LGON** and **LGOF:** a pilot logging on or off.

M → **CLEAR** empties the list.

### GROUP SUP

Code groups to hide from the CODE view. Each entry shows how many targets it matches.

| Entry | Hides |
|---|---|
| `1200` | That one code. |
| `12` | 1200 to 1277. |
| `4000-4077` | That range. |

### HOLD LIST

Holding flights, each with altitude, fix, EFC and minutes holding.
- **Detected:** with M → **AUTO** on, a flight that flies a full turn within 10 NM over 8 minutes is listed, marked `H`, with the nearest fix.
- **Entered:** M → **ENTRY** with `ACID [FIX] [EFC HHMM]`, or `-ACID` to remove.

### SAA FILTER

Special activity airspaces you list, for example `R4001A W386`.
- **Switch:** click one to switch it ON or OFF.
- **Delete:** middle-click one.
- **All at once:** **ALL ON** and **ALL OFF**.

### Fixes and FRDs

Fix names and fix-radial-distance entries (`BOS090010`) are resolved from CRC's own `%LOCALAPPDATA%\CRC\NavData.dat`.
- **Without CRC:** only airports resolve.
- **Bearings** are true, not magnetic.

---

## 15. Working with the vUSALink EDST app

[vUSALink App and vUSALink-Lite](https://github.com/djbrombizzle/vUSALink-App) connect the vUSAlink EDST to CRC. Two of their features also feed the companion.

### CRC CIDs

The vUSALink app learns CRC's CIDs by reading the QF readouts CRC prints, for example `151 SKW5646(32) E75L/L`.

**How it reaches the companion:**
1. The app saves what it learns to `%LOCALAPPDATA%\vUSALinkApp\cids.json`.
2. The companion reads that file whenever it changes. It never writes to it.
3. The CID then shows in:
   - **CFR's FLID**, with the owning sector: `151 SKW5646(32)`;
   - the **CID column** of CPDLC HIST and MSGOUT.

**To get CIDs showing:**
1. Run vUSALink App or vUSALink-Lite alongside CRC.
2. QF the aircraft in CRC: middle-click its data block, or type `QF SKW5646`.
3. Within a few seconds the CID appears in the companion.

The app drops a CID when the pilot reconnects (CRC gives them a new one) or leaves VATSIM, so the companion doesn't keep showing an old CID. Without the app, the CID fields stay blank.

### QF to CFR

The same file tells the companion when you QF an aircraft in CRC. With CFR open, each aircraft you QF (middle-click its data block, or type QF) is added to CFR within a few seconds ([section 10](#10-cfr-continuous-flight-plan-readout)). Both vUSALink App and vUSALink-Lite do this; nothing needs to be set up.

### GI notes

Notes sent with the EDST's **GI** button appear in the companion's UA view ([section 11](#11-ua-update-area)).

---

## 16. Data sources and privacy

| Data | Source | When it's read |
|---|---|---|
| Traffic, flight plans, controllers | VATSIM data feed (`data.vatsim.net`) | Every 15 s |
| Frequencies pilots are tuned to | VATSIM transceivers feed | Every 15 s |
| ARTCC boundaries, prefixes, airports | `vatspy-data-project` | Weekly |
| Fixes, navaids, FRDs | CRC's `NavData.dat` on your PC | At start |
| METARs | aviationweather.gov, with metar.vatsim.net as backup (or the other way round) | Every 2 min |
| CPDLC traffic, GI notes | vUSAlink hub public feed | Every 10 s, only while a CPDLC or UA view is open |
| Airport TMU (ground stops, restrictions, rates) | vatflow-hub (WebSocket) | Live, only while the UA view is open |
| FCA programs | VATFLOW's FCA store | Every 20 s, only while MRP or UA is open |
| MRP route data | vatflow.io | Weekly, cached |
| CRC CIDs, QFs for CFR and macros | vUSALink app's `cids.json` on your PC | Every second while CFR is open or a macro is waiting, else when needed |

**What the companion sends, and what it doesn't:**
- **It never sends anything back.** It doesn't send CPDLC messages, change TMU state, change FCAs, or write to the vUSALink app's files.
- **Macros type into CRC only.** A macro you click types its commands into CRC's MCA on your PC. Nothing goes over the network.
- **Your CID stays on your PC.** It's only used to find you in the public VATSIM data.
- **Service requests carry only your airspace,** for example `KZDC`.

---

## 17. Troubleshooting

| Problem | What to check |
|---|---|
| A view says `NO FACILITY` | Set your VATSIM CID (tray menu) and log on, or use a profile named after your facility (`ZDC`). |
| A view is off screen | Tray menu → **Reset view positions**. |
| Views look smaller or bigger than CRC's | Tray menu → **Size**. |
| Fixes don't resolve in CRR, HOLD LIST or CFR | CRC's `NavData.dat` wasn't found. Install or run CRC once. |
| Weather shows `-M-` | The station has no report, or the last one is over 2 hours old. Try the other **Weather source**. |
| CPDLC views say `CONNECTING TO HUB` or `HUB DOWN` | The vUSAlink hub isn't answering. The views retry every 10 s. |
| UA says `VATFLOW TMU DOWN` | vatflow-hub isn't answering. The companion reconnects on its own. |
| MRP says `LOADING VATFLOW` for a long time | The first open downloads about 5 MB of route data. Check your internet connection. |
| MRP is empty | Your ARTCC has no FCA enabled in VATFLOW. Turn off M → **ACTIVE** to see disabled ones. |
| CFR or CPDLC CIDs are blank | Run the vUSALink app and QF the aircraft in CRC ([section 15](#15-working-with-the-vusalink-edst-app)). |
| A macro says `NOT TYPED` | CRC isn't running, has no ERAM window open, or wouldn't come to the front. If CRC runs as administrator, run the companion as administrator too. |
| Typing goes to the wrong window | Clicking a view never takes the keyboard. Only an open entry box or edit field does. Press Esc to hand the keyboard back to CRC. |
| Something crashed | Errors are logged to `%APPDATA%\CRCCompanion\error.log`. |

---

## 18. Reference

### Number ranges

| Setting | Range |
|---|---|
| LINES | 1–60 |
| COL | 1–6 |
| FONT | 1–5 |
| BRIGHT | 0–100 (steps of 2) |
| ROUTE (IN SECTOR) | 0–80 |
| MIN (INBND) | 5–60 (steps of 5) |
| NM (CONFLCT ALERT) | 3–10 |
| ENTRIES (CFR) | 1–10 |
| RTE (CFR) | 30–100 |

### Files and folders

| Path | Holds |
|---|---|
| `%APPDATA%\CRCCompanion\settings.json` | Your CID, active profile, auto-switch and weather source |
| `%APPDATA%\CRCCompanion\profiles\` | One `.json` file per profile |
| `%APPDATA%\CRCCompanion\cache\` | Map data and VATFLOW route data |
| `%APPDATA%\CRCCompanion\error.log` | Error log |
| `%LOCALAPPDATA%\CRC\NavData.dat` | CRC's navigation data (read only) |
| `%LOCALAPPDATA%\vUSALinkApp\cids.json` | CIDs learned by the vUSALink app (read only) |

### Settings for testing

| Environment variable | Effect |
|---|---|
| `CRCCOMPANION_HUB_URL` | Use another vUSAlink hub (CPDLC and GI). |
| `CRCCOMPANION_VATFLOW_HUB_URL` | Use another vatflow-hub (Airport TMU). |

### Building from source

The project is .NET 8 WPF. GitHub Actions builds it and publishes the exe. To build it yourself:

```
dotnet build src/CrcCompanion -c Release
dotnet run --project tests/CoreTests
```

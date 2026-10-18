# Orbit – SteamVR overlay (csukló-óra, 3 monitor, billentyűzet)

Saját, XSOverlay-szerű SteamVR overlay app. C# / .NET 8, Direct3D11 + Direct2D (Vortice), OpenVR,
Windows Graphics Capture. A felülete angol, ez a leírás magyar.

## Mit tud

- **Csukló-óra** (alapból bal kéz): idő + dátum, fps / reprojekció / GPU ms (a SteamVR compositorból),
  VRAM + GPU-terhelés (WDDM számlálók, mint a Feladatkezelőben), akkuk (HMD, L, R, trackerek közül a legkisebb).
  Akkor jelenik meg, ha a csuklódat magad felé fordítod és ránézel. 4 gomb: billentyűzet, monitorok
  ki/be, recenter, beállítások (a SteamVR dashboardon nyitja meg az Orbit fület).
- **Monitorok VR-ben**: mindegyik monitor külön overlay (nálad a 3 db 1920×1080), alapból ívben eléd
  rakva úgy, ahogy az asztalon állnak (bal Xiaomi, középen az AOC, jobb Xiaomi). Monitoronként: méret,
  átlátszóság, görbület, rögzítés (világ / bal kéz / jobb kéz / fej), egérvezérlés.
- **Billentyűzet**: teljes billentyűzet, a feliratokat a Windows aktív kiosztásából veszi (magyar QWERTZ,
  AltGr-jelek kék színnel). Shift/Ctrl/Alt/AltGr/Win: 1 kattintás = csak a következő billentyűre,
  2 = rögzítve, 3 = ki. Nyomva tartva ismétel (backspace, nyilak).
- **Beállítások**: ugyanaz a felület asztali ablakban (tálcaikon), VR-ben **lebegő Orbit-panelként** (az óra
  fogaskerék-gombja nyitja; lézerrel kezelhető, grippel mozgatható, X-szel zárható, mindig a monitorok
  előtt rajzolódik) és a SteamVR dashboard „Orbit” fülén is.
- **Lézer-simítás** (General → Smoothing: Off / Low / Med / High, alap Med): adaptív One Euro szűrő a
  lézer irányán – nyugodt kéznél erősen simít, gyors mozdulatnál alig késik. Mérve (szimulált remegés,
  1,07 m-es monitor 1,2 m-ről): kikapcsolva ~5 px rezgés, Med ~0,9 px, ~30 ms késés 1 rad/s-nál.
  Kattintáskor a kurzor 0,3 s-ig áll (amíg 0,9°-nál jobban el nem mozdítod), így a ravasz meghúzása
  nem csúsztatja el a klikket.
- **Billentyűhang** (Keyboard fül): egyetlen rövid klikk leütésenként (mint a Virtual Desktop billentyűzetén),
  Click / Muted, hangerő, „Test sound”. XAudio2-vel szól (kis késés), a hangeszközt háttérszálon nyitja.
- **Billentyűzet felső sáv**: Select all / Copy / Paste / Cut / Undo / Redo (Ctrl+A/C/V/X/Z/Y, virtuális
  billentyűkóddal, így QWERTZ-n is a Ctrl+Z a visszavonás). **Chatbox mód**: a gépelés egy saját sorba
  megy (max. 144 karakter, ékezetekkel), Enter / Send → OSC `/chatbox/input` a VRChatnek, közben
  `/chatbox/typing`; Esc törli.
- **Nyelvváltó gomb** (földgömb a szóköz mellett): a Windowsban telepített kiosztások között vált (a
  Windowst is átváltja), és ha az angol nincs telepítve, egy beépített angol (US) kiosztás is van – ez
  a betűket közvetlenül karakterként küldi, a Ctrl/Alt/Win + betű gyorsbillentyűket az angol betű
  virtuális kódjával. A választást megjegyzi (Keyboard.Language).
- **Zene a csuklón**: a Windows médiavezérlőjéből (SMTC) cím, előadó, borító, és előző / lejátszás-szünet /
  következő gombok (Wrist → Now playing).
- **Értesítések**: XSOverlay-formátumú UDP-üzenetek a 42069-es porton (VRCX: Beállítások → Értesítések →
  XSOverlay), a látótér alsó részén kártyaként, max. 3 egymás alatt, halk csengővel; General →
  Notifications (Test gomb). Ha az XSOverlay fut, az foglalja a portot.
- **Animációk**: a panelek be-/kiúsznak (áttűnés + enyhe méretezés), a kapcsolók gombja csúszik, a
  kijelölések csúsznak, a gombok és billentyűk felvillannak / benyomódnak.
- **Gyors ki/be (OVR Toolkit-stílus)**: mindkét grip egyszerre, kétszer gyorsan → a monitorok és a
  billentyűzet eltűnnek; ugyanez újra → visszajön, ami látszott. (General → Double-tap both grips.)
- **Egybe pattintott monitorok**: első induláskor a monitorok egyetlen íves képernyővé állnak össze
  előtted (hézag nélkül). Desktops → *Join into one curved screen* újra összerakja, *Separate* szétszedi.
  Egy monitort egy másik mellé ejtve az automatikusan a sorba pattan; B/Y-t nyomva tartva megfogáskor
  kiveheted a sorból. A sort bármelyik tagjánál fogva egyben viszed.
- **Hajlítás**: Curvature csúszka (kiírja a hajlítási sugarat), *Curve around me* = pont a fejed köré
  hajlít, VR-ben megfogás közben a kar jobbra/balra hajlít. Egy sor mindig közös sugáron marad.
- **Színezés (Look fül)**: 6 kész téma egy kattintással (Classic, Ocean, Sakura, Forest, Ember, Mono),
  kiemelőszín a 10 alapszínből vagy **bármilyen árnyalat** (Custom colour csúszka, szivárvány-sáv alatta),
  panelszín (Charcoal/Midnight/Plum/Forest/Mocha/Black), panel-átlátszóság.
- **Windows 95 stílus** (Look → *Style*): a beállítások, az óra, a billentyűzet és az értesítések igazi
  1995-ös Windowsként – „Orbit Properties” tulajdonságlap, Date/Time ablak a csuklón, MS Sans Serif,
  3D keretek, lenyíló listák –, eredeti pixelméretben rajzolva és simítás nélkül nagyítva. A Modern a régi marad.
- **Lézer** (Look fül): szín, **vastagság** (1,5–8 mm), és hogy **mindig látszódjon** (1 m-es sugár akkor
  is, ha nem mutatsz panelre) vagy csak panelre mutatáskor.
- **Számlapok** (Wrist fül → Watch face): Full (minden), Analog (mutatós óra + akkuk + teljesítmény),
  Minimal (nagy idő + gombok), Time (csak idő, kis kártya), **Custom** (widgetek egy 6×4-es rácson, lásd 1.5.0).
  12/24 órás óra is itt.
  Performance: Numbers (számok) vagy **Graph**: az utolsó 5 mp képkockaideje oszlopokban
  (sárga = reprojektált, piros = eldobott képkocka, vonal = a frissítési keret), a SteamVR `GetFrameTiming`-ből.
- **Egyes ablakok tükrözése** (Windows fül): Add a window → az ablaklistából (mint az Alt+Tab) kiválasztod,
  és külön panelként lebeg VR-ben (max. 6). Követi az ablakot, ha a PC-n mozog vagy átméreteződik,
  bezárás/újraindítás után program + ablakosztály (és utolsó cím) alapján magától újra rácsatlakozik.
  Kattintás előtt a PC-n előtérbe hozza az ablakot, hogy a kattintás biztosan bele menjen
  (a kis-méretre tett ablakot az első kattintás visszaállítja). Méret, átlátszóság, hajlítás, rögzítés
  (világ/kéz/fej), egér ki/be, *Stop mirroring*. A monitorokkal együtt kapcsol (Desktops gomb, dupla grip).
- **Okos képfrissítés** (Desktops → Smart frame rate, alapból be): teljes fps csak arra a monitorra /
  ablakra, amit nézel vagy amire mutatsz; látótér szélén 15 fps, mögötted 4 fps.
- **Szünet levett headsetnél** (General, alapból be): ha a headset 3 mp-ig nincs a fejeden
  (közelségérzékelő), nincs capture és nincs lézer, amíg vissza nem veszed.
- **Lekerekített VR-beállításpanel**: a lebegő beállításpanel sarkai kerekek, vékony világos szegéllyel.
- **Magától feljövő billentyűzet** (Keyboard → *Pop up for text fields*, alapból be): ha egy VR-ből érkező
  kattintás szövegmezőbe teszi a fókuszt, a billentyűzet feljön; ha a fókusz nem-szöveges elemre kerül,
  0,8 mp múlva eltűnik (csak ha magától jött fel, Chatbox módban és gépelés közben nem).
  Felismerés: WinEvent-hook (EVENT_OBJECT_FOCUS) külön szálon; Win32 Edit/RichEdit ablakosztályból, minden
  más (böngésző, Discord, Windows-appok) az akadálymentességi szerepből („editable text”, nem csak olvasható).
  Ez utóbbit csak VR-kattintás után 1,5 mp-ig kérdezi (a Chromium ettől bekapcsolja az a11y-fáját, ami lassít).
- **Éjszakai mód** (csuklón a hold gomb, vagy Look → *Night mode*): minden Orbit-panelt (monitorok, ablakok,
  billentyűzet, óra, beállítások, értesítések, lézer) tompít, áttűnéssel; *Night brightness* 15–90 %.
  A SteamVR overlay-színével (SetOverlayColor) szoroz, a megfogás-színezés megmarad.
- **Elrendezés-profilok** (új *Layouts* fül + a csuklón a profil nevét mutató gomb): Desk / Bed / Couch.
  Minden profil megjegyzi a monitorok, ablakok és a billentyűzet helyét, méretét, hajlítását, és az
  éjszakai módot. Váltáskor a régi profilba elmenti az aktuálisat, az újat visszarakja. Első használatkor
  minden oda jön, ahova nézel (ágyban fekve fölém). A csuklógomb a „On the watch” profilok között lép
  (alapból Desk és Bed).
- **Nyelvgomb javítva**: a billentyűzet nyelvgombja a billentyűzet-*kiosztást* írja ki (HKL felső szó), nem a
  beviteli nyelvet – nálad az angol nyelv + magyar kiosztás eddig „EN” volt, most „HU”.
- **DXOverlay / VRChat avatar-paraméterek (OSC)**: az XSOverlay-jel azonos nevű paramétereket küldi a
  VRChatnek (127.0.0.1:9000): `isOverlayOpen`, `isKeyboardOpen`, `isWristVisible`, `openOverlayCount`,
  `leftControllerBattery`, `rightControllerBattery`, `averageControllerBattery`, `averageTrackerBattery`.
  A DXOverlay ezekből dolgozik, így az Orbittal is működnie kell (VRChatben az OSC-t be kell kapcsolni).

## Kezelés VR-ben

| Mozdulat | Hatás |
|---|---|
| rámutatsz egy panelre | megjelenik a sugár; monitoron az igazi egérkurzor odamegy |
| ravasz (trigger) | kattintás (monitoron bal klikk, billentyűzeten gombnyomás) |
| B / Y | jobb klikk a monitoron |
| kar (stick) fel-le | görgetés |
| markolat (grip) rámutatva | megfogod és viszed a panelt |
| fogás közben kar fel-le | távolabb–közelebb |
| fogás közben kar jobbra-balra | monitor: hajlítás; óra/billentyűzet: méret |
| a másik kéz gripje ugyanazon a panelen | kétkezes átméretezés (széthúzás = nagyobb) |
| B/Y nyomva + grip egy soron lévő monitoron | kiveszed a sorból |
| mindkét grip kétszer | monitorok + billentyűzet ki/be |
| a másik kézzel megfogod az órát | áthelyezed a csuklón (elmenti) |

**Recenter fekve is**: ha egyenesen előre nézel (±15°), a monitorok függőlegesek maradnak; ha jól láthatóan
fel- vagy lefelé nézel (hátradőlve, ágyban), az egész elrendezés a nézésed irányába kerül, feléd fordítva
(fekve föléd). Ugyanígy nyílik a lebegő beállítás-panel és az első elhelyezés is.

A dashboard nyitva tartása alatt a saját sugár kikapcsol (ott a SteamVR sugara működik).

## Indítás

- `dist\Orbit.exe` – asztali parancsikon: `Desktop\Orbit.lnk`. Ha a SteamVR már fut, azonnal
  csatlakozik; ha nem, a tálcán vár, és 3 másodpercenként ellenőrzi. **A SteamVR-t magától nem indítja el.**
- Újraindításkor (második példány) csak előhozza a beállítás-ablakot. Bezárás: tálcaikon → Exit, vagy `Orbit.exe --exit`.
- General → **Start with SteamVR** (alapból be): az Orbit beregisztrálja magát a SteamVR-be
  (`orbit.vrmanifest` → `Steam\config\appconfig.json`, az automatikus indítás:
  `Steam\config\vrappconfig\erik.orbit.vrappconfig`). Ezután a SteamVR indításakor magától elindul
  (tálcán, ablak nélkül, a feladaton át admin joggal), és a SteamVR leállásakor kilép. SteamVR nélkül is
  átkapcsolható, ilyenkor a következő SteamVR-induláskor alkalmazza. A SteamVR saját helye:
  Beállítások → Indítás/Leállítás → Indításkor futó overlay-alkalmazások → Orbit.

## Fájlok

- Beállítások: `%LOCALAPPDATA%\Orbit\config.json` (mentéskor az előző `config.json.bak` lesz;
  hibás fájlt félretesz `.bad-…` néven, nem törli). Log: `orbit.log` (+ `orbit.prev.log`).
- Forrás: `src\` – `OrbitApp.cs` (főciklus, SteamVR-kapcsolat, elhelyezés), `Panels\` (óra, billentyűzet,
  monitor), `Vr\` (overlay, input, lézer), `Ui\` (widgetek, beállítás-képernyő), `Capture\` (monitor- és ablak-capture, ablaklista).
- Input-akciók: `assets\actions.json` + alap bindingok Quest/Touch, Index és Vive kontrollerekre.

## Telepíthető verzió (barátoknak)

`build_release.ps1` → `release\OrbitSetup.exe` (~75 MB, egyetlen fájl, a .NET is benne van – a másik
gépen semmit nem kell előre telepíteni). Futtatva angol telepítőt mutat (asztali parancsikon / Start with
SteamVR / Run as administrator), felhasználói szintre telepít admin jog nélkül:
`%LOCALAPPDATA%\Programs\Orbit` + Start menü + Gépház > Alkalmazások (onnan eltávolítható, az
eltávolító leveszi a SteamVR-regisztrációt és az admin feladatot is, a beállításokat megtartja).
Mellé kerül az angol `Orbit Quick Start.txt`. Nincs digitálisan aláírva, ezért a Windows SmartScreen
először figyelmeztet („További információ” → „Futtatás mindenképp”).
Ugyanaz az exe: ha a neve tartalmazza a „setup” szót, telepítő; telepítve `Orbit.exe` néven maga a program.

## Build

```
dotnet publish -c Release -o dist
```
(Előtte `dist\Orbit.exe --exit`, mert a futó exe zárolja a fájlt.)

## 1.3.0 – GitHub, frissítés, ellenőrző kör (2026-10-13)

- Repó: https://github.com/Erik1028/Orbit (nyilvános, MIT). A felhasználói README angol, ez a fájl a
  magyar fejlesztői jegyzet. A `docs/vr-test-*.png` (az asztal tükrözött képe) nincs a repóban.
- **Frissítés az appon belül** (General → Version sor gombja, `Sys\Updater.cs`): GitHub API
  `releases/latest` → ha újabb, a gomb „Update to x.y.z”: letölti az `OrbitSetup.exe`-t a
  `%LOCALAPPDATA%\Orbit\updates` mappába, ellenőrzi (méret, GitHub SHA-256 `digest`, ProductName = Orbit,
  ProductVersion), és `--update --install-dir <a futó példány mappája>` kapcsolóval elindítja. A telepítő
  leállítja az Orbitot, kicseréli a fájlokat (beállítás, parancsikonok, Alkalmazások-bejegyzés marad –
  utóbbiban csak a verzió frissül, ha ez a telepített példány), majd újraindítja (`--steamvr`, ha a SteamVR
  fut). Fejlesztői `dist` mappában is működik: az egyfájlos exe a régi DLL-ek mellett is rendben fut
  (kipróbálva). `--updatetest`: lekérdezés + letöltés + ellenőrzés telepítés nélkül; a telepítő
  `--no-start` kapcsolója tesztekhez.
- **Ellenőrző kör** (5 szempont, mindegyik találatot külön ellenőr próbálta cáfolni): 30 találatból 27
  megerősítve (25 különböző), mind javítva. A fontosabbak: a fókusz-hook újrabelépése (régebbi esemény
  felülírta az újabbat – most sorszám, `--focustest`-ben látszik a „superseded”), profilváltás két
  menetben (a sor nem szakad szét), a profil által nem ismert panelek eléd kerülnek, első használat
  headset nélkül később pótolva, foglalt 42069-es port: 10 mp-enként próbálja újra (nem képkockánként,
  socket-szivárgás nélkül), elesett monitor-capture újraépül (30 mp-es visszatartással), a médiafigyelő
  később érkező borítót is betölti, a kattintás utáni képkocka mindig újrarajzol, kép-rögzítés
  gyorsulásnál azonnal frissít, 256 px-es kurzor, kiosztás-variánsok (0xFxxx) a registryből, ablakcím-
  változás nem ír lemezre (MarkSoftDirty), sikertelen mentés újrapróbálva, duplikált ablak-azonosító
  kiszűrve, egy hibás overlay nem állítja meg az indulást.

## 1.3.1 – teszt-frissítés (2026-10-13)

- Frissítés után az Orbit első indításkor VR-értesítést mutat („Orbit updated – Now running 1.3.1
  (was 1.3.0)”), és a General fül Version sora alatt „updated from 1.3.0” áll. A régi verziót a telepítő
  olvassa ki a felülírt `Orbit.exe`-ből, és `--updated-from` kapcsolóval adja tovább (az admin-feladaton
  át is); tartalékként a konfig `General.LastVersion` mezője.

## 1.3.2 – zárható óra (2026-10-13)

- Wrist → *Lock position* (`Wrist.Locked`): a grip nem mozdítja és nem méretezi át az órát
  (`WristPanel.Grabbable` = !Locked); zárt panelre griplésnél egy rövid haptikus tick jelzi, hogy zárva van.
  A *Reset position* zárva is működik. Az 1.3.1-es frissítést Erik élesben kipróbálta (19:09, dist-ben): rendben.

## 1.3.3 – DXOverlay csak valódi overlayre (2026-10-13)

- Az OSC `isOverlayOpen` / `openOverlayCount` mostantól csak a látható monitorokat, tükrözött ablakokat
  és a billentyűzetet számolja; az Orbit saját lebegő beállításpanelje nem (Erik: a DXOverlay
  elindult, amikor csak a beállításokat nyitotta meg).

## 1.15.0 – új widgetek, fókusz mód, mozi minden stílusban (2026-10-18)

- **Új widgetek**: Photo, Weather, Folder (`WatchFolders.cs`), Friends online + Session recap (VRCX, `immutable=1`),
  On this day (`PhotoMemories.cs`), Temperatures (ADL + PawnIO), Light (`RoomLights.cs`: Lumos HTTP 8770, vagy közvetlenül
  `TapoDirect.cs` – KLAP, csak a SHA256(SHA1(e-mail)+SHA1(jelszó)) kulcs DPAPI-val, a sorszám InvariantCulture-rel: a hu-HU
  mínusz U+2212!), RGB (Prisma pipe), Avatar kapcsolók (`AvatarToggles.cs`: OSC JSON + VRChat OSCQuery, a 9001-et nem foglalja).
- **Paletta**: kategóriák + keresés (az asztali ablakban gépelve).
- **Fókusz mód** (`GazeFocus.cs`): rajzolási felülírás mint a TheaterView, a mentett elrendezést sosem írja; 0,3 s rátekintés,
  15 % szélsáv, a szélső monitor a helyén nő; `ORBIT_SNAP_ONLY=focus`.
- **Mozi mód**: Ambilight (`Ambilight.cs`, sort 8), stílusonkénti háttér (`TheaterBackdrop*.cs`), Auto theater
  (`AutoTheater.cs`, alapból ki), `TheaterWarm.cs` előre rajzol. Cyber: városélet (`TheaterCityLife.cs`, 5 Hz folt-frissítés,
  autók), hangra lélegző horizont (`HorizonPulse.cs`), médiakonzol a sávon (`TheaterBarMedia.cs`), a padló csak elöl 156°-ban
  és 14°-ig lefelé, 3 külön overlayen (`TheaterFloor.cs`: a SteamVR az íves overlay-t N lapos oszlopból rajzolja – a teljes
  körön a padló körei sokszögek lettek). Rétegsorrend: fal 1, háttér 2, padló 3, sáv 4, gyűrű 5, szikrák 6, autók 7,
  fény 8, képernyők 106+. `ORBIT_SNAP_ONLY=cyfloor` szimulált headset-nézet 16/32/64 oszloppal.
- **Teljesítmény**: `VrOverlay.Calls` + `PerfTrace` + `ORBIT_SNAP_ONLY=theaterperf` (költségkerettel); Modern billentyűzet
  0,84 → 0,13 ms, beállítások csak változásra (`LiveSig`), belépés a mozi módba 23 → 1,5 ms, 0 B/képkocka a mozi módban.
- **Javítások** (átnézés 10-18): TapoDirect átirányítás nélkül, 64 KB-os válaszkorlát; `config.backup-<verzió>.json`
  frissítéskor; a Recenter mozi + fókusz módban a mentett helyhez igazít; a cyber fények gyors ki-be kapcsolásnál nem
  villannak; a tesztek tényleg elbuknak hibánál (KLAP álizzó-teszt).

## 1.14.1 – Notifications nagy nézet, Cyber mozi-háttér, billentyűzet a kijelzők alá (2026-10-17)

- **Notifications** kinyílik (`Panels/WristNotes.cs`, `ORBIT_SNAP_ONLY=notes`): az utolsó 20, Clear, stick-görgetés;
  a retró stílusok közös postafiók-ablaka a skin-ekkel, a terminál `tail notify.log`.
- **Cyber nagy óra** (`Panels/WristCyberDetail.cs`) + Cyber Now playing / frame rate / system nézet.
- **Theater fal**: zárt henger (curvature 1, 2πr széles, `WallAspect` 2), `Theater.WallAlpha`: 55°-ig sötét, 80°-ra eltűnik;
  a fekete textúra nyersen írva (premultiplied). Cyberben (Erik: „eléggé low effortnak néz ki”) éjszakai város
  (`TheaterCyber.cs`): a falon ég + neon horizont + szintihullám-padló (a padló sugarai a hengeren függőleges vonalak,
  a körei a horizont felé sűrűsödő sorok), külön, élesebb sáv-overlayen (`erik.orbit.theater.city`, 4096×512, sort 2) a
  városkép körben + retró nap 120°-nál (nem a képernyő mögött). EGYSZER rajzolódik; belépéskor a fal pislákolva
  „bekapcsol” (csak alpha), a város fényei utána (Full).
- **Recenter**: `PlaceKeyboardUnder` – a szemtől a legalsó kijelző alja alá húzott vonalon, 0,9 m-re, feléd fordítva.
- **cyber-polish**: a Painter szövegrenderelése képkockánként egyszer (~0,4 ms minden panelen), a Cyber billentyűzet és
  beállítások nyugalomban textúrából (2,39 → 0,16 ms, 2,15 → 1,01 ms), új HUD-animációk; `ORBIT_SNAP_ONLY=cyberperf`.

## 1.14.0 – magyar nyelv, keverő, figyelmeztetések, Cyber HUD, saját ablakkeret (2026-10-16)

Erik: „1 mehet, 2 később, 3 mehet, 4 is mehet” (nyelvválasztó, hangerő mindenhol + keverő, figyelmeztetések, B/Y kétszer,
játékonkénti elrendezés, óraoldal export/import), majd a Cyber teljes redesign (beállítások, billentyűzet, panelek,
animációk; a Full/Analog/Minimal/Time számlapok is), apró UI-hibák, ablakkeret minden stílusban. Az 1.13.2 csak piszkozat
volt (nem jelent meg): a logó-pontja ide került. Ágak: extras, mixer, cyber-faces, cyber-ui, language2 → main.
- **Nyelv**: `src/Lang` (`Lang.T` / `Lang.F`, `Hu.cs` tábla), General › Language; a HUD-zsargon (LINK, SYS) angol marad.
- **Hangerő VR-ben**: `Sys/AudioMixer.cs` (lásd lent a „mixer” részt); kapcsoló: Wrist › „Windows volume in VR”
  (`General.VolumeToApps`: null = auto, "off"). TRAP (javítva): a Windows egyetlen MMDeviceEnumerator objektumot ad a
  folyamatnak – a `new XxxCom()` egy második osztályban „Specified cast is not valid”-ot dobott (a keverő VR-ben el sem
  indult; 1.12.3 mikrofon). `CoreAudio.NewEnumerator()`: típus nélkül, aztán interfész-cast. VR-ben ellenőrizve.
- **Ellenőrző**: `--snapshot` „overlaps” sorai (`Ui.HitSpy`): egymást keresztező vezérlők / szekciócímek minden fülön minden
  stílusban (+ Modern/Cyber asztali ablakként); `ORBIT_SNAP_ONLY=overlaps`.
- **Ablak**: keret nélküli minden stílusban (`SettingsWindow`: a Modern/Cyber fejléc a caption ahol nincs vezérlő –
  `Ui.OverWidget`; saját kis méret / bezárás; nincs WS_MAXIMIZEBOX).
- **Lapjelző**: `WatchWidgets.PageStrip` (26 px) a többoldalas modern órákon.
- **Default look** TRAP (javítva): az új `AppearanceConfig` LookVersion 0-t írt → a következő verzió 1.10-es migrációja
  rózsaszínt tett Erik zöldje fölé.

## Következő – hangerőkeverő widget, a Windows hangerő VR-ben (2026-10-16, `mixer` ág, nincs kiadva)

- **A Windows hangerő VR-ben** (`Sys/AudioMixer.cs`): a Virtual Desktop hangszórója hardveres hangerőt jelez és nem
  alkalmazza (1.12.1), ezért a Windows csúszka, a hangerőgombok és a Hexpad gombja (mind a végponti hangerőt állítja)
  VR-ben semmit nem csináltak. Most, amíg a SteamVR fut, az app fő ciklusa (`OrbitApp.Tick` → `AudioMixer.Tick`) figyeli
  a végponti hangerőt és némítást (`RegisterControlChangeNotify` callback + 4 Hz-es nézés biztonsági hálónak), és ha más
  állítja, az eszköz minden appja követi – mindegyik a saját arányával: egy app, amit külön lehalkítottál (Windows
  keverő vagy az óra keverője), annyival halkabb marad, a többi pontosan a szintet kapja (mint egy rendes eszközön).
  Később induló app a szintet kapja; egy appot, amit valaki külön állított, nem állít vissza. Az Orbit saját változtatása
  (az óra hangereje, a keverő felső sora) a végpontra is megy (a Windows csúszka az igazat mutatja) és rögtön az appokra,
  és nem hat kétszer (saját event-context GUID; a callback kiszűri). Induláskor a master a leghangosabb app szintjét
  veszi át (amit hallasz) – semmi nem lesz hangosabb/halkabb magától. A `SystemVolume` app-módja ezért az `AudioMixer`-en át
  megy (a saját munkamenet-kódja megszűnt); az órák (`WristPanel.Volume`) alapból kitaláltak, az app adja át a valódit
  (`UseRealAudio`).
- **Melyik eszköz**: `General.VolumeToApps` (csak a configban, nincs kapcsoló a beállításokban): null/"auto" = a
  Virtual Desktop eszköze név szerint, ha hardveres hangerőt jelez; "on" = minden eszköz, ami hardveres hangerőt jelez
  (csak az hagyhatja figyelmen kívül – a többin a Windows maga alkalmazza, ott a követés duplán halkítana); "off" = egyik
  sem. Kapcsoló a General fülön minden stílusban: később (a nyelvválasztó munka közben nem nyúltam a beállításlapokhoz).
- **Hangerőkeverő widget** (`mixer`, „Volume mixer”, `Panels/WristMixer.cs`): az alapértelmezett kimeneti eszköz appjai
  (munkamenetek exe szerint csoportosítva, a SteamVR / Steam folyamatai egy sorban), saját hangerő (`ISimpleAudioVolume`),
  név (a munkamenet neve, az exe FileDescription-je vagy neve) és az exe ikonja (`SHDefExtractIcon`, háttérszálon). Csúszka
  appokként (lézerrel húzható, a stick 2 %/notch), az ikonra bökés némít; elöl a szóló és leghangosabb appok (2 s-onként
  rendez, csak ha egy másik jól hangosabb; amíg a lézer a widgeten van, a sorrend áll). Az Orbit saját munkamenete (a
  billentyűhang) nincs a listában (a hangereje a Keyboard fülön van), a követés viszont azt is viszi. Méretek: 2×1 (a két első egymás
  mellett), 2×2, 3×2 (lista), 4×3-on M és L; kinyitva a kimeneti eszköz hangereje + minden app (4 felett két oszlop, a
  stick görget). Modern Bento (widget-kinézettel), Cyber (tag, szegmensek, cián jel), a retro stílusok a 95-ös úton: sorok
  95-ös trackbarokkal; kinyitva a Windows 95 Volume Control (sndvol32) – oszloponként app, álló trackbar (a skin saját
  trackbarja transzponálva: a fény bal-fent marad, a hegye jobbra mutat), Mute, vízszintes görgetősáv (a skin
  görgetősávja transzponálva) – minden skin a saját vezérlőivel. Terminálban nincsenek ikonok (zöld szöveg).
- **Eszközök**: `--snapshot` (kitalált appokkal): `mixer<tag>` sorok (sorrend, húzás, némítás, stick, kinyitás, a kinyitott
  nézet húzása / némítása / görgetése) minden stílusban, `mixer sync` sorok (kitalált eszközön: kívülről jövő változás
  egyszer az appokra, a saját nem visszhangzik, új app, külön állított app, némítás, melyik eszköz), képek
  `wrist_mixer_<eset><tag>.png`. `ORBIT_SNAP_ONLY=mixer`: csak a keverő minden stílusban (pár másodperc).
  `--mixertest`: a valódi eszköz, ahogy az app olvassa – CSAK OLVAS (nem ír semmit, a szinkron nem fut).
- **Nincs VR-ben kipróbálva**: a követés a valódi VD eszközön (a kód útja kitalált eszközön tesztelve, a valódi
  eszközön csak olvasva).

## 1.13.2 – az új logó mindenhol (2026-10-16, csak piszkozat: az 1.14.0-ban jelent meg)

Erik: „a logó amit mondtam még nem változott meg”. 1.13.0-ban csak az exe ikonja (`assets\orbit.ico`) és a
`Tray.MakeIcon` lett új; a SteamVR dashboard gombja (`OrbitApp` `_thumbSurf`), a beállítások fejléce, a telepítő még a
régi gyűrű + pötty volt, a 95-ös `W95Icons.Id.Orbit` (95 About, Windows 7 címsor) a régi bolygó. Most mind
`Painter.Logo(r)` (az SVG 64-es rácsán, mindig simítva), a 95-ös ikon kézzel rajzolt 16 x 16-os változat.

## 1.13.1 – Cyber minden stíluslistában (2026-10-16)

A Windows 7 / Mac / terminál beállítás a `Skins.StyleIds`-t használta (6 elem); most a `StyleMenu`-t (7, Cyberrel), a
Windows 7 csempéi a 7-hez szűkülnek, a Cyber csempét a `CyberHud.Chip` rajzolja.

## 1.13.0 – öt új stílus, animációk, Mozgás (2026-10-16)

Erik: „egy nagy munka: több windows stílus” (retró koncepció) + a Cyber HUD koncepció (cyber_1/2), majd „minden
kapjon stílusához illő animációkat teljes körűen, a cyberpunk főleg”. Ágak: retro-base (alap: `RetroSkin` réteg a
95-ös primitívek alatt, `Theme.Retro`, `AppearanceConfig.Style` xp/aero/mac/terminal; leírás docs/retro-styles-brief.md)
→ style-xp/-aero/-mac/-terminal (párhuzamos ügynökök, saját worktree) → style-cyber (a modern renderelő testvére:
src/Gfx/Cyber.cs, src/Panels/WristCyber.cs) → anim-core/-xpaero/-macterm/-cyber → minden a retro-base-en, aztán main.
- **Mozgás**: `AppearanceConfig.Motion` (null = full | reduced | off), `Motion.Full`/`Motion.Any`; Off = a rugók egy
  képkocka alatt célba érnek; minden stílus beállításában (modern szegmens, 95/XP rádiógombok, 7 lenyíló, Mac pop-up,
  terminál `MOTION [FULL]` sor).
- **Egy „következő képkocka” szabály**: `AnimWake` – egy animáció kéri a panelje következő képkockáját; nyugalomban nincs
  extra rajzolás (a mérés: nyugalomban +0,01–0,02 ms).
- Eszközök: `--animstrip <dir> [--only-style ...]`, `ORBIT_SNAP_ONLY=anim_cyber`, `ORBIT_BENCH_STYLE`/`_MOTION`, a
  `--snapshot` 'layout xp/aero/mac/term/cyber' sorai.
- **Új ikon**: Erik `orbitoverlay.svg`-je (assets/), `orbit.ico` minden méretben külön renderelve, `Tray.MakeIcon` ugyanaz.
- **Mikrofon/hangerő**: hiba után az eszközlista is újraépül (1.12.3-ban a mikrofon `DefaultChanged`-ben elakadt).

## 1.12.3 – folyamatos Hexpad az órán (2026-10-15)

Erik: „a hexpad elég kevés fps-el mozog”. Az óra csak `_dirty`-ra vagy az időzítőre (0,1 / 0,5 s) rajzolt újra, a
Hexpad új képei (`Snapshot.Version`) nem tették dirty-vé → 2–10 kép/s. Most a dock-verzió változása dirty, ha a dock
widget az aktuális oldalon van vagy a dock nézet nyitva (a Hexpad legfeljebb 20/s-mal küld, 50 ms-os push).

## 1.12.2 – a szoba és a Hexpad az órán nyílik ki (2026-10-15)

Erik: „az órán a vrchat szoba is egy kiugró widget legyen, nem külön ablak”, „a hexpad is lehetne órán lévő kiugró
widget”. Ág `room-popout` (worktree `Orbit-room`): a room és a dock widget koppintásra a megnyitott nézetbe nő ki
(mint az aknakereső; 95-ben az 1995-ös zoom-keret), X / mellé koppintás visszazsugorítja.
- Szoba: a `RoomPanel` adatai és sorrendje (néhány segédje megosztva), két oszlop, a kar görget; 95: „Room - világ”
  lista-ablak állapotsorral. A RoomPanel a beállításokból még nyílik (ott vannak a csillagok).
- Hexpad: a `DockPanel` bemenet-kezelése változatlanul a közös `DockInput` osztályba került; az óra ugyanazt az egy
  `HexpadLink` kapcsolatot használja. 6 gomb (136 px-es arcok), 3 kerek gomb, 3 tekerő csúszkaként; 95: képkeretek +
  trackbar-csoportok. A DockPanel csak akkor látszik, ha a configban nyitva maradt. `--docktest` előtte/utána 52 kép
  pixelre azonos.

## 1.12.1 – hangerő Virtual Desktoppal (2026-10-15)

Erik: „maga a windows hangerő állítás nem megy”. Mérve (`scratchpad/volprobe.ps1`, `sessprobe.ps1`): a
„Speakers (Virtual Desktop Audio)” `QueryHardwareSupport` = 0x1 (hardveres hangerő), a végponti hangerő beállítható
és megmarad (0,95 olvasható vissza), de a meghajtó nem alkalmazza – ezért a Windows saját csúszkája sem hat.

- **`SystemVolume` app-mód**: ha az eszköz neve „Virtual Desktop”, `IAudioSessionManager2` → minden munkamenet
  `ISimpleAudioVolume`-ja (a hangerőkeverő app-csúszkái, a hangmotor a meghajtó ELŐTT alkalmazza): egy szint
  mindre; az első nézés a leghangosabb app szintjét veszi át; másodpercenként az új appok is megkapják
  (`GetSessionInstanceIdentifier` szerint); a némítás is appokra. `PerApp` jelzi. `--voltest [dip]`: olvas, (dip:
  2 s-ra −5%), visszaállít.
- **Mikrofon**: a COM-kasztolás `as` (1.12.0: „Specified cast is not valid”, a widget sosem látta a mikrofont);
  hiba esetén a napló a helyet is írja.

## 1.12.0 – oldalak, új widgetek, óra-stílusok, Bento 3. lépés (2026-10-15)

Erik: „gyorsan végezni annyi felírt ötlettel amennyivel csak lehet”, „még ne legyen kiadás, majd ha mindennel
végeztünk”. Párhuzamos ügynökök, mindegyik saját git worktree-ben (`Orbit-<név>`, ág = a feature neve), gyors
saját ellenőrzéssel; review-kör nélkül; a merge-ket én oldottam fel a main-en.

- **Bento 3. lépés** (`bento-step3`): `Kind.Extra` – csak modern méretek (idő 1x1/2x2, dátum 1x1; `SizesFor`,
  `Clean` az unióra, 95-ben a legközelebbi rajz), idő S/M/L (dátumsor + „VR 1:23”, másodperc az alapvonalon,
  másodperc-csík), dátum S/M, naptár hétcsík (2x1) és havi rács (2x2, régió szerinti hétkezdés), idő VR-ban S/M;
  másodperc-határ utáni újrarajzolás csak ha van másodperces widget. Snapshot en-US és hu-HU, 12/24 h.
- **1x1 analóg** (`analog1x1`): Erik képe alapján (macOS squircle óra): `Painter.FillSquircle/StrokeSquircle`
  (n = 4, méretenként gyorsítótárazva), 60 vonás a belső squircle-ig, narancs #FF8C32 másodpercmutató.
- **Óra-stílusok** (`clock-variants`): `WatchWidget.Variant`; idő: Standard/Flip/Outline/Stacked/Segment/Words;
  analóg: Squircle/Classic/Minimal/Roman/Station; nem illő méreten az alapértelmezett rajzol (a változat megmarad);
  szerkesztőben „Clock” sor. `--clockshot`, `--perfbench --bench-variant`.
- **Aknakereső** (`minesweeper`): `Mines.cs`/`WristMines.cs`/`MinesTest.cs`; 1x1 alap, koppintásra kinő a csempéből
  (modern: rugó, 95: 1995-ös zoom-keret); 9x9/10 és (csak modern) 16x16/40; első kattintás biztonságos, chord,
  zászló hosszú nyomással (0,4 s) vagy Flag mode; legjobb idők `WristConfig.MinesBest`; 95: winmine.exe kliensterület
  pixelre (168x211), a címsor/menü mellé került.
- **Oldalak** (`pages`): `WristConfig.Widgets` = 1. oldal (bájtra változatlan), `Pages`/`Grid`/`Name`/`Page`/
  `PagesVersion` csak beállítva íródik; `WatchPages.Migrate` a konstruktorban (UI nélkül!): régi confignál ELŐBB
  `config.json.bak-pages-<idő>`, aztán VRChat + Performance (6x4) egyszer; új telepítés: custom face, VRChat 6x4 +
  Performance 6x4. Lapozás: húzás (>20%), pöttyök, 95-ben tálca-gombok. `GridSpec` 6x4/4x3 oldalanként,
  `Sizes43`. Új fajták: mic (Windows felvevő eszköz némítása, Core Audio), notifications (20-as előzmény),
  buttons (`WatchWidget.Items`). Média idővonal (GSMTC timeline), borító-színátmenet, 30 s képidő-grafikon 1% low-val.
- **Betűtípus** (`font-setting`): `AppearanceConfig.Font`, `Theme.Font/Display`; a szélesebb fontok kicsinyítve
  (Segoe szélességére), táblázatos számjegyek; snapshot minden fonttal (`ORBIT_SNAP_FONTS=0` kihagyja).
- **Kisebb telepítő** (`installer-size`): `SatelliteResourceLanguages=en` + a WPF és a WinForms designer kiszedése a
  bundle-ből (`OrbitDropUnusedDesktopRuntime` target): 75,7 → 49,1 MB.
- **Javítások**: ablak áthúzása monitorok között (`IPointerTarget.TakesDragFrom/AdoptDrag`: a szomszéd monitor
  átveszi a lenyomott gombot); a beállításablak mozgatása VR-ból nem fagyaszt (WM_ENTERSIZEMOVE alatt 10 ms-os
  timer hívja a ciklust – `SettingsWindow.ModalTick`); a játék-input blokk utóideje 1,5 s csak monitor/ablak után,
  0,3 s a kis panelek után, azonnal ha a panel eltűnt; a képernyő-sáv csak a középső monitor alatt (`TheaterAnchor`,
  másodpercenként) + billentyűzet-gomb.
- **Félretéve**: nyelvválasztó (ág `language`, worktree `Orbit-lang`, félkész – Erik: „egyelőre hagyjuk későbbre”).

## 1.11.0 – widgetenkénti kinézet + Bento 2. lépés (2026-10-15)

Erik: „legyen minden widget külön színezhető” (a lista 7. pontja), majd „lehessen több stílust beállítani, 2 tónus,
egyéb designok per widget”. Folyamat: workflow (megvalósítás a `bento-step2` ágon, `Orbit-bento2` munkafa → kód-,
látvány- és ellenőrző-review: 19 találat, 1 magas → javítás → végső ellenőrzés), scratch: `scratchpad/step2`.

- **Painter**: `FillGradient` (2/3 stop, szög, span; `ID2D1LinearGradientBrush` gyorsítótár, LRU 64 fölött, téma-
  váltáskor ürül; 95-ben sima Fill), `CircleGradient`; `GradientsMade` számláló (nyugalomban 0 új ecset/frame).
  Az alap kártya felső fénye: `Bento.CardTop` (Lift + 0,035) → Card a felső felén.
- **Config**: `WatchWidget.Style` (null = Bento kártya | tint | solid | twotone | header | outline | glass),
  `Color`, `Color2` (uint?) – csak beállítva íródnak (`JsonIgnore WhenWritingNull`), Erik configja bájtra azonos.
- **`Bento.Look`** (struct): a kártya festése (stopok, szög, fény, keret, fejsáv) + minden token, amit egy widget
  olvas (Text/Sub/Faint/Tile/Track/Accent/AccentFill/OnAccent/Data/Cpu/Mem/...). `Bento.Make(style, szín, szín2,
  saját, éjszaka, kártyák)` → a WristPanel kulcs szerint gyorsítótárazza; `Bento.Use`/`Reset` a stílusos widget
  körül (a `finally` is visszaállít). Alap widget nem hív `Use`-t → a step-1 színek változatlanok.
- **Olvashatóság**: kitöltésen fehér vagy #0B1220 szöveg (az egész átmenetre nézve), a kitöltés sötétül/világosodik
  4,8:1-ig; Warn/Bad színes kártyán sötét korong „!”-lel (sosem tűnik el, nincs piros érték piros kártyán); gombok:
  solid/two-tone KI = sötét változat, BE = teljes szín + pipa; csak-akció gombok (Recenter, Layout, Settings) teljes
  kitöltéssel. Glass „Cards only” háttérrel sűrűbb (világos világ felett is olvasható). Éjszaka a színek −40 %.
- **Szerkesztő** (csak modern): kijelölt widgetnél 7 stílus-csempe előnézettel, Auto + 13 szín, two-tone-nál második
  szín, „Use on all widgets”; a Background és a „Start over” csak kijelölés nélkül látszik.
- **QA**: `--snapshot` stílusgaléria (`qa/styles_*.png`), 604 stílusos számlap elrendezés- és kontrasztellenőrzése
  a szöveg nélküli renderhez mérve (4,5:1, nagy/vastag/ikon 3:1), lebegő kártyák sötét szobán és fehér falon is.
  `--perfbench --bench-style <look|mixed>`. Mérve: Erik számlapja 0,72 ms (main 0,69–0,70), a legrosszabb stílusos
  (header) 0,80 ms. 95: az eltérések csak élő óra/CPU területek (21 kivágás átnézve).
- Hátra: Erik VR-ban nézze meg a stílusokat (különösen Glass „Watch panel” háttérrel), a Bento 3. lépés (idő/dátum/
  naptár családok), majd a lista többi pontja.

## 1.10.1 – javítás: indulás az 1.10.0-ra frissítés után (2026-10-15)

- Az 1.10.0 első indulása `NullReferenceException`-nel állt le: a `LookVersion`-átállás a `NoteVersion`-ben (a
  konstruktorban) `AppearanceChanged()`-et hívott, ami `_win.Dirty`-t állít – az ablak ekkor még nem létezik. A config
  nem íródott (a mentés előtt állt meg). Most `Theme.Apply` (az ablak és a dashboard később úgyis ebből rajzol).
  TANULSÁG: a konstruktorban futó kód ne hívjon ablakot/dashboardot érintő metódust; a frissítés-utáni indulást
  (`--updated-from`) a kiadás előtt ki kell próbálni.
- `NoteVersion`: ha a config `LastVersion`-je régebbi az `--updated-from`-nál (egy verzió mentés előtt leállt), a
  régebbit veszi → a „What's new” az 1.10.0 pontjait is mutatja.

## 1.10.0 – Bento redesign, 1. lépés (2026-10-15)

Erik: „először is lenne egy redesign … ne másold le teljesen, mivel vannak hibái, a színek, formák és design vonulat
legyen ilyen” (Bento widgetkészlet-koncepció; leírás: memory orbit-wishlist-1015).

- **Folyamat**: feltérképezés (3 ügynök) → terv (`scratchpad/bento_spec.json`: tokenek, betűskála, formák, widgetenként,
  13 lépés) → kritika (18 pont, a fontosak beépítve) → az 1. lépés külön munkaágon (`Orbit-bento`, `bento-step1`) →
  2 QA (kód + 95-ös azonosság; látvány) → 10 javítás (2e685b9) → újraellenőrzés → 2 maradék javítás (RAM/VRAM egység
  a feliratsorban, a megnyitott óra nézet dátuma méretre igazítva).
- **`Gfx/Bento.cs`** (csak modern): a felületek a Look panelszínéből keverve egy hűvösebb, világosabb árnyalat felé
  (`Lift` 0,15 egy hangolható érték); Text #F2F3F7, egy szürke #9EA4B6, Ok #3DDC84 / Warn #F7C948 / Bad #FF5A5A,
  CPU lila #8B7CF8, RAM/VRAM kék #5AA9FF, AccentFill + OnAccent, DangerFill/Edge. **Bento téma** (#FF5D93 / #0A0B10) a
  Sakura helyén, ez az alapértelmezett és a „Default look”. `AppearanceConfig.LookVersion`: frissítéskor egyszer
  Bento-ra vált (a régi színek a naplóba kerülnek), új telepítés eleve azzal indul.
- **Óra (modern)**: lap r26 + 2 px él; kártya r16, lapos, ~6%-os fehér hajszálkeret; feliratok NAGYBETŰS, ritkított 15 px
  SemiBold szürke bal fent; értékek vastagon balra, egység az alapvonalon; minden más ≥ 17 px (csempefeliratok 15-ig
  szűkülhetnek); 1x1 = felirat + érték (30 px) + egy vonal; RAM/VRAM egysége a feliratsorban. Állapot a közös
  küszöbökből (`LevelOf`/`FrameLevel`), a Warn/Bad az érték szövegét is színezi; akku: Ok > 30, Warn 16–30, Bad ≤ 15 a
  kiírt egész számon; töltés = villám. Gombcsempék felirattal, BE = accent kitöltés fehér vastag felirattal. Minden
  vezérlő ≥ 40 px találati terület. Hangerő 2x1/3x1: −/+ 44 px korongok + csak olvasható sáv. Nincs időzítő-kör
  (Erik 10-14-én elvetette). Megnyitott nézetek a kártyák színeivel; a fix számlapok is az új akkuszabállyal.
- **Painter**: ritkított szöveg (DirectWrite karakterköz, gyorsítótárral), `TextLine` alapvonal-metrika, `ClipText`
  (az óra szövegei vágás nélkül → nyugalomban 0,69 ms a redesign előtti 0,89 helyett).
- **95-ös stílus pixelre azonos** (157 kép, csatornánként, időfüggő részek maszkolva; az eltérések óra/CPU/RAM és a
  verziószám). `--faceshot <dir>`: Erik saját számlapja a config MÁSOLATÁBÓL, saját és Bento színekkel.
- Hátra (következő lépések, külön kiadásokban): a kártya felső fénye + vonalgrafikon (2), idő/dátum/naptár családok
  (3), …, az Apple-stílusú 1x1 analóg (9), majd Erik listájáról a widgetenkénti szín és az állítható rács.

## 1.9.4 – húzás két lézerrel egy képernyőn (2026-10-15)

Erik listájáról: „ha mind a kettő kontroller a kijelzőre mutat, … egy ablakot … a kijelző tetejére húzni hogy full
screenbe rakjam, ha túlhúzom a kurzort az egér átugrik a másik kontrollerre, akkor is ha azzal nem is kattintottam”.

- **Ok**: a sugár elhagyja a panelt → `PointerLeave` → `MouseOwner` törlődik → a másik kéz sugara (ami a panelen van)
  `OwnsMouse`-zal azonnal átveszi, miközben az első kéz még nyomja a gombot → a húzott ablak a másik lézerhez ugrik.
  Ugyanez, ha a húzó kéz 0,4 s-nál tovább áll.
- **Pointer capture** (`Laser`): amíg a ravasz nyomva van egy panelen, az megtartja a sugarat a szélén túl is
  (`VrOverlay.ProjectLocal` → `LocalRaycast.Intersect(clampToEdge: true)`: lapos panelen a síkra, ívelten a
  hengerre vetít, és a szélre szorít) → a kurzor a monitor legfelső sorára kerül → Aero Snap teljes képernyő.
- **`MouseOwner.Held`**: amíg a tulajdonos kéz nyom egy egérgombot, más kéz nem veheti át (`OwnsMouse`, lenyomás), és a
  `PointerLeave` sem törli a tulajdonost. Monitor- és ablakpanel egyaránt.
- `--capturetest`: 12 ellenőrzés (lapos/ívelt, fent/balra/sarok, rögzítés nélkül nincs találat) – a valódi egérhez
  nem nyúl.

## 1.9.3 – „What's new” frissítés után (2026-10-15)

Erik listájáról: „Frissítés után egy popup hogy mi változott”.

- **`docs/CHANGELOG.md`** (angol, felhasználói nyelven, a GitHub kiadási jegyzetekből tömörítve, 1.3.0-tól) beépítve
  az exébe (`whatsnew/CHANGELOG.md` erőforrás, nem a `files/` alatt, így a telepítő nem bontja ki). Formátum:
  `## <verzió> | <cím>`, `- ` pontok, behúzott sor = folytatás. `Sys/WhatsNew.cs`: Parse, `Since(from)` (from < v <=
  futó), `Latest(n)`. **Minden kiadásnál új szakasz kell bele.**
- `NoteVersion`: frissítéskor `General.WhatsNewFrom` = a régi verzió (ha két frissítés jön OK nélkül, a régebbit
  tartja); ha nincs bejegyzés, marad a régi kártya. OK után null.
- **VR**: `Panels/WhatsNewPanel.cs` – 0,8 m-re előtted, kicsit lejjebb (`GazeFrame`, mert magától jelenik meg, nem az
  óráról), 3 s-mal a csatlakozás után, ha a headset rajtad van; megfogható, stick görget, OK eltünteti; a helye nem
  mentődik. **Asztal / VR beállítások / dashboard**: `SettingsView` modális ablak (`Ui.Inert` az oldal alatt), modern
  kártya elsötétített háttérrel, 95-ben „What's New in OrbitOverlay” párbeszédablak `List95`-tel (valódi 95-ös
  görgetősáv), szótördelés a 95 betűvel (a vastag címek is). General fül: „What's new” / „What's New...” gomb (az
  utolsó 4 verzió). Közös rajzoló: `Ui/WhatsNewView.cs`; `Painter.MeasureHeight` (tördelt szöveg magassága, gyorsítótárral).
- Snapshot: `settings_whatsnew(_95).png`, `whatsnew_panel(_95).png`.

## 1.9.2 – a hangerő widget a VR hangeszközét állítja (2026-10-15)

Erik: „valamiért a hangerő widgetek nem működnek” – „csak VR-ban a Virtual Desktop Audio driver a fő driver, azt
valamiért nem állította”.

- Mérve (csak olvasva): VR nélkül az alapértelmezett eszköz „NGENUITY - 8 Channel Spatial (HyperX Virtual Audio
  Device)” 50% – a widget ugyanezt mutatta; egy 1%-os próba (50 → 49, visszaolvasva 0,3 és 1 s múlva, majd vissza
  50-re) szerint az állítás meg is marad. A „Speakers (Virtual Desktop Audio)” eszköz VR nélkül inaktív.
- **Ok**: `SystemVolume` az eMultimedia szerepkör alapértelmezett eszközét kérte, és csak 5 s-onként nézett rá újra; ha
  a Virtual Desktop csak a fő (eConsole) szerepkört állítja át, az Orbit tovább a HyperX-et tekerte. Most: eConsole
  (amit a Windows hangerő-ikonja is), másodpercenként összeveti az eszköz-azonosítót (`IMMDevice.GetId`), váltáskor
  azonnal átveszi. A naplóba írja, melyiket kezeli (`volume: now controlling '<név>'`, a név a registryből, csak
  olvasva), és ha az eszköz elutasítja (HRESULT) vagy nem veszi át a szintet (visszaolvasva > 2% eltérés), azt is –
  eszközönként egyszer.

## 1.9.1 – az overlay a headset ütemében frissül, nem a játékéban (2026-10-14)

Erik: „bármi ötlet hogy az overlay még reszponzívabb legyen és több fps-el rendereljen?”, majd az ajánlott 1-esre:
„mehet”.

- **Mérés előtte** (`--pacetest`, élőben, csak olvasva): `WaitFrameSync` = a VRChat képkockái (63–83/s), a SteamVR
  „vsync” számlálója is ennyi, a kompozitor 75–85-öt ad ki (a többi újravetített), a kijelző 90 Hz. A VRChat GPU-ja
  10,2–10,5 ms a 11,1-ből, a kompozitor 0,47 ms, az OrbitOverlay ~1% GPU (PDH, AMD-n alulmér, de arányaiban jó). A
  `WaitFrameSync` mindig a képkocka legelején tért vissza (fázis-hisztogram), így a frissítés ~10 ms-ot várt a
  kompozitálásig; időtúllépéskor pedig +5 ms alvás → ~35 ms-os megakadás.
- **`Sys/FramePacer.cs`**: nagy felbontású waitable timer (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`), saját 1/Hz-es
  rács; késésnél egy pótlás, egy egész képkockányi elmaradásnál új rács. A fő ciklus ezzel fut (`General.HeadsetPacing`,
  alapból be; ki = a régi `WaitFrameSync`). Headset nélkül (`PauseWhenOff`) 20 Hz. `--pacertest`: 89,7 tick/s, átlag
  11,11 ms, szórás 0,23, max 11,86 (VRChat mellett).
- A perf sor kiírja a játék és a kompozitor fps-ét is (`GetCumulativeStats`), így látszik a három szám egymás mellett.

## 1.9.0 – sáv a képernyők alatt: mozimód az óra nélkül (2026-10-14)

Erik: „a theater mode gomb lehetne a megnyitott kijelző alatt egy pár beállítással együtt” (az óra-rácsa tele van).

- **Sáv a monitor / ablak alatt** (`Panels/TheaterBar.cs`, most két szerepben): ha a lézer egy világba tett
  monitor- vagy ablakpanel alsó ~28%-án van (`Laser.HoverPx`, felső-bal origó), alatta feltűnik: **Theater** gomb
  (azzal a képernyővel indul a mozimód) + a mozimód beállításai: **Dim** (0–90%), **Size** (0,8–8 m), **Curved**.
  2 s-mal az utolsó mutatás után eltűnik; másik monitor alsó részére mutatva átugrik oda. Kézre/fejre tett panel alá
  nem jön (lemaradna mögötte). Mozimódban ugyanez a sáv a váltó: monitor/ablak gombok + a beállítások élőben + Exit.
  Húzás közben (és egy stick-lépés után 0,4 s-ig) a sáv a helyén marad, a méret nem húzza ki a lézer alól; a sáv
  1 cm-rel a vászon előtt van (a növekvő vászon nem takarja el). A stick egész lépésekben állít (egy lépés = 2%).
- **Config**: `Theater.Curved` (ív be/ki → `TheaterView.Flat`), `Theater.Bar` (a sáv be/ki). `Theater.Apply` a
  méretet és az ívet élőben alkalmazza; a fogással méretezett vászon mérete minden képkockán visszakerül a configba
  (`MarkSoftDirty`), így a Dim/Curved állítása nem ugratja vissza, és mindenhol ugyanaz a szám látszik.
- **Beállítások → Layouts → Theater mode** mindkét stílusban (a sáv ki/be, Dim, Screen size, Curved;
  `IOrbitHost.TheaterEdited`). A 95-ös Layouts csoportok alacsonyabbak lettek, alattuk a Theater mode csoport.
- Kódátnézés (3 szempont + cáfoló ellenőrzés) 9 valós hibát talált, mind javítva: a Dim/Curved visszaugratta a fogott
  méretet; a csúszka 6 m-ig ment, a fogás 8-ig; a sáv nem frissült, ha máshol állítottál; **NullReference**, ha a
  panel overlay-e megszűnt, miközben alatta halványult a sáv; szerepváltáskor egy beragadt „elengedés” rossz értéket
  írhatott (`Cancel()` törli a latch-eket); kézre tett panel alatt lemaradt; a stick képkockánként lépett; a növekvő
  vászon eltakarhatta a húzott sávot; az elengedés a sávon.
- `--pacetest <s>`: élő, csak olvasó mérés: mi üteme a ciklust. Erik gépén (VRChat): `WaitFrameSync` = a játék
  képkockái (63–83/s), a kompozitor 75–85/s-ot ad ki, a VRChat GPU-ja 10,2–10,5 ms a 11,1-ből, a kompozitor 0,47 ms,
  az OrbitOverlay ~1% GPU. Az fps-t a VRChat GPU-ja korlátozza, nem az Orbit.

## 1.8.2 – mozimód: rendes áttűnés, nincs oldalra úszás (2026-10-14)

Erik élőben (napló: 6 váltás 6 s alatt): „kicsit blokkosodva vált a monitorok között, valamint az oldalra úszó
animáció fura”.

- **Hova kerül**: a monitorsor közepe elé (`OrbitApp.TheaterAnchor`: a Recenterrel azonos választás – fő monitor,
  csoportban a sor közepe), bármelyik monitor játszik. Az 1.8.1 a játszó monitor irányába tette: oldalsónál oldalra.
- **Be/ki** (`Theater._glide`, induláskor és kilépéskor újra eldöntve): ha a játszó monitor saját helye 25°-on belül
  van a vászon irányától (a középső), abból nő ki / abba megy vissza (0,45 s). Különben helyben tűnik fel / el
  (0,3 s, 90 → 100 % nagyítással, az ív a mérettel együtt, ugyanaz a sugár); kilépéskor utána otthon halványan jön
  vissza (`TheaterVanish` → `_vis = 0`), nem ugrik be teljes erővel. Oldalra úszás nincs.
- **Váltás = valódi áttűnés**: az új a régi FÖLÖTT halványul be (sort 107 vs 106, `TheaterView.Behind`), a régi
  szilárdan alatta marad, amíg az új teljesen látszik (`TheaterFade` ≥ 1 és ≥ 0,1 s; max 1 s), aztán eltűnik. Az
  1.8.0/1.8.1 mindkettőt egyszerre halványította → félúton átlátszott a háttér (fényerő-beesés), és a régi közben a
  saját méretére ugrott (2,80 → 1,07 m, `--ovdump`-pal látva).
- **Friss kép váltáskor**: mozimódban a félreállt panelek 3 fps-sel tovább rögzítenek (`warm`), így a választott a
  mostani képével jön, nem a rejtéskor látottal (az pár pillanat múlva „ugrott” az újra).
- Halványulás közben a panel megtartja az utolsó szélességét, ívét, átlátszatlanságát (`_opacity`).
- `VrOverlay.SetSortOrder` csak változáskor hív a SteamVR-be.
- `--theatertest`: 20 ellenőrzés (középső kinő, oldalsó helyben, váltáskor a régi alul szilárd, majd megy; kilépés
  oldalsóval helyben; a vászon a középső elé kerül).

## 1.8.1 – a mozimód vászna megjelenik; simább animációk, gyorsabb rajzolás (2026-10-14)

Erik: „még dolgozz az animációkon és sebességen, stabilitáson”, majd az 1.8.0-t kipróbálva: „a theather mode nem
működik, nem jelenik meg ablak”.

- **Mozimód, a vászon nem jelent meg** (napló: 4× be/ki, hiba nélkül; a váltósáv megjelent 2,6 m-re; Erik félig
  fekve, a monitorok fölötte döntve, `DesktopsVisible` ki). Két gyanús pont, mindkettő javítva:
  1. **Hova kerül**: az 1.8.0 a `GazeFrame` szerint tette, vagyis amerre a koppintás pillanatában nézel – az pedig
     mindig az óra (fekve, felemelt karral: a plafon felé). Most: a fejedből a lejátszott monitor/ablak saját helye
     felé, annak dőlésével (`Theater.Where`); kézre/fejre tett panelnél vízszintesen előre. A Recenter mozimódban
     ugyanezt használja (a frissen eléd hozott elrendezés után). Az ív a valódi 3D távolságból (fölötted is a
     fejedre központos).
  2. **A sötétítés**: az 1.8.0-ban egy fejhez rögzített fekete lap volt 0,5 m-re, sort order 1. Ha a SteamVR nem
     (csak) a sort order szerint rajzol, a lap a vászon elé kerül. Most: széles, félkörben köréd hajló fekete fal
     0,6 m-rel a vászon MÖGÖTT (szélesség π·r, ív 0,5) – sorrend és távolság szerint is a vászon alatt van.
  Napló: `theater: on - <név> (overlay …, fade …, picture …, capture …)` és a beúszás után `theater: playing …`;
  `--ovdump <s>`: csak olvasva kiírja a futó OrbitOverlay overlay-einek SteamVR-állapotát (látható, alfa,
  szélesség, ív, sort, textúra, hely és távolság a fejtől), változáskor.

- **Mérés előbb** (`--perfbench <dir>`: Erik saját óra-elrendezése a config MÁSOLATÁBÓL, 90 Hz-es ütemben: beúszás
  hidegen/melegen, nyugalom, minden widget kinyitva; CPU és „kész a GPU-n” idő). Egy órarajzolás ~1,85 ms volt, ebből
  ~1,5 ms a szöveg: képkockánként ~29 `DrawText`, mindegyik újratördeli a szöveget (~50 µs). Szöveg nélkül 0,38 ms.
- **Szövegelrendezés-gyorsítótár** (`Painter.Text`, csak a modern út): kulcs (szöveg, formátum, szélesség,
  magasság) → `IDWriteTextLayout`, `DrawTextLayout`; 1000 felett újrakezdi, formátum-ürítéskor vele dobja. A 95-ös
  GDI-mért pixelszöveg változatlan. Snapshot-összevetés RGBA mind a 4 csatornán: 100 widget-kép bitre azonos, az
  eltérők mind élő adatot mutatnak (óra, CPU/RAM, idő VR-ben). Eredmény: nyugalom 1,85 → 1,23 ms, beúszás 2,23 →
  1,8 ms.
- **Az óra animációi a headset rátáján** (eddig max. 45 fps volt, spórolásból): a beúszás, a rugók, a görgő
  számjegyek most 90 Hz-en mennek.
- **Mozimód**: kilépéskor a többi panel csak a siklás utolsó harmadában jön vissza (a hazatérő vászon nem halad át
  rajtuk); a váltósáv kicsit felcsúszva jelenik meg, a modern gombok kiemelése/hover/nyomás animált (a világító
  gomb átúszik a választottra); a sáv régi textúrája csak az új átadása után szabadul fel; a gomblista képkockánként
  szemét nélkül épül.
- **Stabilitás**: Erik 1.7.1-es 36 perces VR-futásában 1 figyelmeztetés (`WaitFrameSync: TimedOut` induláskor), 0 hiba;
  a ciklus 90 alatti tempója (61–74/s) akkor is megvolt, amikor az Orbit 0,15 ms-ot dolgozott → a GPU-terhelés
  (VRChat/kompozitor) korlátoz, nem az Orbit.

## 1.8.0 – Mozimód (2026-10-14)

Erik az ötletlistából: „3-as mehet esetleg” (mozimód).

- **Mozimód** (`Panels/Theater.cs`, óra-gomb `theater`, `WristAction("theater")` → `OrbitApp.ToggleTheater`): a
  monitor vagy ablak, amin utoljára a lézer volt (`_lastScreen`, a lézerek `Hovered`-jéből), különben a fő monitor,
  egyetlen nagy, ívelt képernyő lesz előtted: alapból 2,8 m széles 2,5 m-re, `GazeFrame` szerint (fekve is), az
  ív a fejedre központosít (`c = w / (2π r)`). A többi monitor/ablak panel félreáll (`TheaterHidden`, a szokásos
  0,2 s-os elhalványulással), a billentyűzet elmegy (és utána visszajön, ha ő tette el), a világ elsötétül: egy a
  headsethez kötött fekete overlay 0,5 m-re, sort order 1 (minden Orbit-overlay alatt), alfa `Theater.Dim` (0,6).
  Az óra, az értesítések, a beállítás/dock/szoba panelek maradnak. Ki-be 0,45 s-os siklás (pozíció lerp, forgás
  slerp, szélesség/ív/átlátszatlanság lerp).
- **Nem ír a mentett elrendezésbe**: a képernyő helye/mérete felülírás (`TheaterView`) a panel `Cfg`-je mellett.
  Fogás mozimódban: mozgatás és tolás/húzás a memóriában (az ív követi a távolságot), stick balra/jobbra és két
  kéz = méret (0,8–8 m); nincs sor-igazítás, nincs `MarkDirty`. Ha fogás közben indul vagy áll le a mozimód, a
  fogás többi része nem csinál semmit (`_grabView`). Kilépéskor csak a `Theater.Width`/`Distance` mentődik (a
  következő alkalomra). A Recenter mozimódban a képernyőt is eléd hozza. A Monitors gomb és a dupla markolás
  előbb kilép a mozimódból (azonnal). Ha a képernyő eltűnik (monitor újraépül, ablak bezárul), magától kilép.
- **Váltósáv a képernyő alatt** (Erik közben: „a theater módban lehessen gyorsan és kényelmesen váltani a monitorok
  között egy kijelző alatt megjelenő gombbal”; `Panels/TheaterBar.cs`): gombonként egy monitor (balról jobbra, ahogy
  az asztalon állnak; azonos nevűek sorszámot kapnak) és a tükrözött ablakok, a játszó kiemelve, a végén kilépés.
  Megjelenik, amíg a lézer a képernyőn vagy a sávon van (és belépéskor 4 s-ig), utána 2,5 s múlva elhalványul. A
  látott magassága állandó (0,05 × távolság), ugyanúgy ívelt, mint a képernyő (`c = w·c_k / w_k`), sort order 110.
  Modern: pirulák; 95: tálca-szerű gombsor (a játszó benyomva). Váltás (`Theater.Switch`): az új ugyanoda és
  ugyanakkorára kerül, a régi a helyén halványul el (rejtett panel nem mozgatja az overlayt) → áttűnés.
  `--theatertest <dir>`: VR nélkül, kitalált headsettel és képernyőkkel (indulás, siklás, fogás, váltás, kilépés,
  eltűnő képernyő) + a sáv képei mindkét stílusban.
- **`Product` = OrbitOverlay** (a híd vége: az 1.7 updatere mindkét nevet elfogadja; az 1.6-os ezt már nem
  telepítené). A release-be továbbra is felkerül `OrbitSetup.exe` néven is.
- README: a mozimód, és a Hexpad-sor javítva (a tekerők az 1.7.0 óta csúszkák).

## 1.7.1 – a kurzor nem ugrál középre (2026-10-14)

Erik VR-ben: „ha nem mutatok a kijelzőre, a kurzor folyamatosan próbál vissza ugrálni középre” – a „Keep clicks from
the game” kikapcsolásával megszűnt (ő próbálta ki). Ok: amikor a SteamVR visszaadja a bemenetet a VRChatnek (a lézer
lemegy a panelről), a VRChat újra középre zárja a kurzort; a panelek szélén, a rések fölött, gépelés közben ez ki-be
billegett. Javítás: a tiltás 1,5 s-ig kitart a lézer távozása után (`OrbitApp._blockUntil`), így csak egyszer adja
vissza, amikor végeztél. Mellette: a szoba-figyelő nyitva tartja a naplót és újrahasznosítja a puffereit (~2 MB/s
háttér-allokáció volt).

## 1.7.0 – OrbitOverlay: szoba, státuszsor, nincs véletlen kattintás, dock-csúszkák (2026-10-14)

Erik: „1 mehet, 2 legyen kapcsolható, 4 de csak az élesebb képek, a dockon … legyen slider”, „amíg az overlayt
használom, a vrchatben ne kattintsak semmire”, „az app neve is lehetne Orbitoverlay” (→ OrbitOverlay, teljes
átnevezés a GitHub repóval együtt).

- **Átnevezés**: exe `OrbitOverlay.exe` (`AssemblyName`), minden látható felirat, telepítő, `.lnk`-k, Start menü,
  README, a GitHub repó `Erik1028/OrbitOverlay` (a régi név átirányít). Maradtak a láthatatlan azonosítók: a
  beállítások mappája `%LOCALAPPDATA%\Orbit`, a SteamVR app kulcs `erik.orbit`, az overlay-kulcsok, az Apps
  registry-kulcs `Uninstall\Orbit`, az admin task neve `Orbit (admin)`, a mutex/eventek. **Híd egy kiadásra**:
  a fájl `Product`-ja még „Orbit”, mert az 1.6-os updater csak ilyet telepít (`fv.ProductName != "Orbit"`),
  és a release-ben `OrbitSetup.exe` néven IS ott a telepítő (a régi updater azt keresi). Az 1.7-es updater
  mindkét nevet és mindkét terméknevet elfogadja → 1.8-ban a `Product` átállítható. Frissítéskor a telepítő
  `MoveFromOrbit`: admin task → új exe (az in-app update emelt jogú), `Orbit.lnk` → `OrbitOverlay.lnk`
  (csak ami a régi exére mutatott), Apps bejegyzés, majd a régi `Orbit.exe` és Quick Start törlése. A SteamVR
  magától követi: a manifest (`binary_path_windows`) már az új exét írja, az app pedig újraregisztrál, ha az
  útvonal eltér.
- **Ki van a szobában** (`Sys/RoomWatcher.cs`, `Panels/RoomPanel.cs`, widget `room`): a legfrissebb
  `output_log_*.txt` követése háttérszálon (FileShare.ReadWrite|Delete, tartós UTF-8 dekóder, csak időbélyeggel
  kezdődő sorok). Indításkor visszafelé keres az utolsó `[Behaviour] Entering Room:`-ig (a napló 54 MB is lehet),
  onnan csak állapot, esemény nélkül. Érkezés: mindenki egy csomóban jön 11–74 s-mal a belépés után → egyetlen
  „Arrived” esemény, ha 3 s-ig nincs új join (max 180 s). `OnLeftRoom` és `HandleApplicationQuit` után nincs
  kilépés-esemény. Saját magunk: VRCX tábla előtagja (`usr<hex32>_friend_log_current`) vagy a napló
  `post login checks for <név> (usr_…)` sora. Barátok: VRCX `VRCX.sqlite3` a Windows `winsqlite3.dll`-jével,
  `immutable=1` csak-olvasás (soha nem zárol), 5 percenként. Csillag: `Room.Starred` (usr_ id). Értesítés:
  barátok/csillagosak (alap), mindenki (opció); érkezéskor 1 kártya. `--roomtest`: kitalált napló (80-as
  hullám, zárójeles név, OnPlayerLeftRoom, új naplófájl) + a valódi napló/VRCX csak olvasva.
- **Státuszsor** (`Sys/StatusLine.cs`, widget `status` = kapcsoló, Settings > VRChat): `/chatbox/input` (azonnal,
  hang nélkül), csak változáskor (min. 3 s), 15 s-onként frissít; most szól / óra / idő VR-ben; max 144 karakter;
  szünetel, amíg a billentyűzet chatbox-módjában gépelsz, és 20 s-ig a saját sorod után; kikapcsoláskor törli.
  Erik VRChat-naplói szerint az OSC ki van kapcsolva – azt VRChatben kell bekapcsolni.
- **Nincs véletlen kattintás a játékban** (`VrSession.UpdateActionSets`, `OrbitApp.BlockGameInput`,
  `General.BlockGameInput`, alapból be): amíg egy kéz lézere Orbit-panelen van / fog, azon a kézen az akciókészlet
  `0x01000000` (k_nActionSetOverlayGlobalPriorityMin) prioritással fut, kézenként `ulRestrictedToDevice`-szal.
  Felengedésig marad (különben a még nyomott ravaszt a játék új nyomásnak látná). Ha a SteamVR nem fogadja a
  kézenkénti bejegyzést, mindkét kézre vált (naplózva). VR-ben még nem próbálta senki.
- **Beállítások**: új „VRChat” fül mindkét stílusban (TabVrchat = 6, General = 7). A widget-paletta 6/sor (27 fajta).
- **Dock: csúszkák a tekerők helyett, éles képek** (Hexpad 44364d6 + Orbit `DockPanel`/`HexpadLink`; két ügynök
  párhuzamosan, az Orbit-rész külön worktree-ben). Mirror **api 2** (visszafelé kompatibilis): hello `caps`
  [hi, values, set, jog]; `faces.hi` = tiszta 2x (136 px) JPEG a kliensnek, ha kérte (Hexpad a renderelőkből veszi:
  `render_face`/`folder_face` `scale`, `live_face` a saját 3x-éből, idle-képernyő 2x csempék; a dockra menő kép
  bájtra ugyanaz), null = a dock 68 px-es képe (animációk, HUD); `controls.encN.slider` abs|jog|"", `kind`;
  `values` (v 0..1, muted, off); kliens `set` (fényerő: `set_brightness_live` + mentés a végén, hangerő: az egyetlen
  sysvol szálon, monitor: csendes `_MonitorDimmer`, kikapcsolt monitornál eldobva; csak ha a tekerő aktuális fajtája
  egyezik) és `jog` (±10, gyorsítás nélkül; a távoli tekerés soha nem gyorsul). Orbit: abs csúszka (húzás =
  set 30 Hz-ig, a végén final; 0,6 s-ig nem veszi át a visszajövő értéket), jog csúszka (24 px = 1 lépés, page/media
  60 px, visszaugrik), a nyomás gomb a sor jobb szélén (veszélyesnél megerősítés); 95-ben Group95 + saját trackbar.
  A modern panel és az óra-widget az éles képet rajzolja, a 95 a dock saját pixeleit 1:1. Élőben: api 2, 6 éles kép,
  hangerő/monitor értékek jönnek; set/jog élőben NEM ment (Erik eszköze) – VR-ben kell kipróbálni. Telepítés:
  UAC; a `Start-Process -Wait` a Hexpad-ra is vár (gyereke) → a várakozót le kell állítani, a Hexpad fut tovább.

## 1.6.0 – a Hexpad stream dock VR-ben (2026-10-14)

Erik backlogjának 1. pontja („ajazzdock compatibility, screen on overlay”), döntései: tükör + lézeres irányítás,
lebegő panel, a veszélyes műveletek VR-ből megerősítést kérnek.

- **Hexpad oldal** (ajazz-dock e943e27, csak helyben commitolva – a repo publikus): új, KÜLÖN pipe
  `Hexpad_mirror_v1` (`dock/mirror.py`, QLocalServer UserAccessOption, soronként egy JSON). A régi
  `AjazzDock_ipc_v1`-hez nem nyúltunk – arra bármi más mint "status" feldobja a konfigurátort, ezért Orbit csak a
  NEVÉT keresi a pipe-listában, sosem nyitja meg. Képcsap az `AKP03`-ban: `set_key_image` / `clear_*` → függő,
  `flush` → egy tuple-csere (`mirror = (gen, 6 JPEG)`), `sleep`/`wake` → `mirror_dark`. Bemenet: `inject()` sorba
  teszi, az eszköz-szál dolgozza fel (a HID írás egyszálú), így a hold / double-tap ugyanúgy működik; `request_wake()`
  kilép az idle képernyőből anélkül, hogy bármit elsütne (különben az első nyomást lenyelné). Kliens alatt a ciklus
  25 ms-re pollol. `status()`: api 1, folder, display_on, ambient. A `controls` üzenet minden gomb / tekerő
  funkcióját küldi (`_action_summary`, `_CTRL_SHORT`), `danger` = bármely gesztus "system" vagy Quick
  lomtár-ürítés / zárolás (makrón belül is). Teszt: `tools/test_mirror.py` (37 ok, hamis HID + saját pipe-név).
  Telepítés: `build.ps1` + önemelő `install.ps1` (UAC), a config bájtra azonos maradt.
- **Orbit oldal**: `Sys/HexpadLink.cs` (háttérszál, aszinkron pipe, JPEG → visszaforgatás `Rotate270FlipNone`,
  mirror, kalibrációs eltolás vissza; csak akkor kapcsolódik, ha a panel nyitva van vagy van Hexpad widget; a pipe
  létezését 3 s-onként nézi), `Panels/DockPanel.cs` (800×480; modern: sötét lap, 3×2 gomb, kerek gombok alattuk,
  nagy tekerő jobb fent, két kicsi alatta; 95: „Hexpad - <profil>” ablak, 68×68-as képdobozok 1:1, tekerők
  Group95 + ◄ Push ► gombokkal, státuszsor). Lézer: lenyom/felenged a valódi gesztusokhoz, a tekerő bal/jobb
  harmada forgat (tartva ismétel), közepe nyom, a stick a tekerő fölött forgat; rámutatáskor `wake`.
  Megerősítés: „Run “System: sleep”?” – a megerősítésig semmit nem küld. `HexpadFaces` = közös bitmapek a panelnek
  és az óra-widgetnek. Új widget `dock` (1x1/2x1/2x2), a palettán csak ha a Hexpad pipe létezik.
  `--docktest <dir>`: élő, csak olvasó kapcsolat (csak hello) + minden állapot hamis képekkel, mindkét stílusban.
- Mérve élőben: kapcsolódás 0,1 s, 6 arc 68×68, 12 vezérlő; `wake` → idle ki 70 ms alatt. Gombot / tekerőt élőben
  NEM nyomtunk (Erik eszköze) – azt VR-ben kell kipróbálni.

## 1.5.2 – vonalak a gyűrűk helyett, teljes vizuális átnézés (2026-10-14)

Erik: „aránytalanul kicsik a kijelzett akkuszázalékok… ellenőrizd át rendesen, ne legyenek ilyen gondok”, majd
közben: „ezek a kör alakú bar kijelzések nem a legjobbak, lehetne csak egy sima vonal a szöveg alatt”.

- **Gyűrűk helyett vonal** (Modern): `Meter` = halvány sáv + balról kitöltés (rugós), `Gauge` = nagy szám, alatta a
  vonal, alatta kicsiben a mértékegység; `Reading` (2x1) = név balra + megjegyzés jobbra, nagy szám, vonal. Akkuk:
  soronként név (⚡ ha tölt, piros ha alacsony) + százalék + vonal, egy widgeten belül egy betűméret
  (`BatteryRowSize`); 1 eszköz / 1x1 = nagy szám. Időzítő, hangerő, stat-widgetek, kinyitott nézetek ugyanígy; a
  kinyitott hangerőnél a csúszka maga a vonal. Az analóg óra számlapja maradt. `Orbit()` törölve.
- **Hibák az átnézésből** (226 kép mindkét stílusban + 4 ügynökös vizuális QA, majd újrarenderelés):
  - `_wid` gyorsítótár 32 felesleg felett *mindent* törölt → élő widgetek új azonosítót kaptak és újra „beugrottak”
    40 %-ról. Most csak a törölt widgetek kerülnek ki.
  - Kilógó / szélre tapadó: 95 Timer „Start” levágva (most ▶ gomb + szóhoz méretezett -1 / Reset / +1), modern
    Timer 2x2 gombjai 14 px-re a széltől, 95 stopper / Mute pipa felhúzva, 95 teljesítmény-sáv rövid feliratokkal
    („86 fps”, „4%”), 95 dátum rövidül évszám nélkülire mielőtt levágná (`FitDate95`), 95 grafikon felirata.
  - Kicsi számok: idő-widget AM/PM 20 px margóval; stat 1x1 számai; kinyitott akku; hangerő 3x1 csúszka (húzható).
  - Átfedés: naptár nyilai a hónap mellett (nem a bezáró X alatt); 95 CPU felirat mögött fekete, rács alatta;
    95 ablak pontosan a rácsot fedi (nem lóg ki mögüle widgetsarok); 95 X gomb nagyobb láthatatlan célterület.
  - Üres sávok: kinyitott Timer jobb oszlop középre, Stopwatch üres köridő-táblázat fejléccel, 95 Power Meter
    csoportdobozban középen, 95 CD Player (magas) hangerő-sorral, 95 Timer / Volume gombsorok feljebb.
  - Egyéb: „since 2:20 PM” mint az óra (nem „du.”); GPU-sáv a keretidő arányában (mint a felirat); naptár annyi
    sorral, ahány hét; CPU nézetben nem kétszer ugyanaz (átlag + csúcs az utolsó percből, memória: szabad);
    VRAM / RAM 1x1 megkülönböztethető („GB VRAM” / „GB RAM”); 95 akku: töltés = rendben (kék), félkövér %,
    alacsonyon is látszik 1 blokk; teli 95 sáv középre igazítva.
- `--snapshot`: `qa/` mappába minden widget × méret × stílus + kinyitott nézetek + nehéz esetek (1-6 akku, hosszú
  cím, semmi nem szól); a dokumentációs képhez előbb CPU-mérés. Elrendezés-ellenőrzés: 0 hiba mindkét stílusban.

## 1.5.1 – minden belefér (2026-10-14)

Erik a kinyitott lejátszón látta, hogy a „következő” gomb lelóg, és kérte: semmi ne lógjon le és ne legyen túl közel
a széléhez. `--snapshot` most automatikusan ellenőrzi (`Tools.LayoutCheckRun`): minden widget minden méretben,
mindkét stílusban, egyedül, háttér nélkül → ami a kártyán kívül nem átlátszó, az kilóg; Modernben a kártya
szélétől 3–8 px-en belüli (kinyitott nézetben 3–14 px) eltérő színű képpont = túl közel. Kinyitott nézetek
vágás és sötétítés nélkül (`WristPanel.LayoutTest`). Igazolva, hogy jelez: a régi lejátszó-elrendezésre 404 px-t
talált. Javítva: a lejátszó gombjai a szabad szélességhez méreteződnek, a borító legfeljebb a szélesség 44 %-a;
mérők/gyűrűk 16 px margó (a „műhold” is bent marad); az időzítő-gyűrű; az akkuk százaléka; a szerkesztő
palettáján ikon csak ha mellette is van margó. Eredmény: 0 hiba Modernben és 95-ben.

## 1.5.0 – widgetes óra (2026-10-14)

Erik backlogjából (memória: orbit-backlog.md) a „sarok-widgetek” és a „az egész óra egy üres felület
widgetekkel” pont; utána: „több widget, szebb, Apple-szerű animációk, szebb összkép”.

- **Custom számlap** (`Wrist.Face = "custom"`, `Wrist.Widgets` = típus + rács X/Y/W/H, `Wrist.Background`
  panel|cards). 6×4-es rács, minden rés és a keret is pontosan 10 px (Modern; a 34 px-es óralap-sarokkal a 24 px-es
  kártyasarok így koncentrikus), 95-ben 4 px; a cellaszélek egész pixelen (Erik: „minden widget széle egyforma
  távolságban”). `Panels/WatchWidgets.cs`: 24 fajta (méretek, alapméret, tónus: akcent / logó-indigó / a kettő keveréke / zöld), Add (a jelölt cellába bármely
  befér méretben), Free/InGrid (túlcsordulás-biztos), Step, SnapSize, Seed (a régi számlapokból + „Corners”), Clean
  (rossz méret → a dobozán belüli legnagyobb megengedett; ismeretlen típus MARAD - egy újabb Orbit widgetje túléli).
- **Widgetek**: idő, dátum, naptár (havi rács / nagy nap; 95: tépőlapos naptár), analóg óra, **időzítő** (gyűrű,
  ±1 perc, lejártakor értesítés + csengő: `timer.done`), **stopper**, **VR-ben töltött idő** (`PanelData.VrSince`),
  akkuk (gyűrűk; kevés hely → a legüresebbek), most szól, **hangerő** (`Sys/SystemStats.cs` SystemVolume: Core Audio
  IAudioEndpointVolume, 5 mp-enként újra lekérve), teljesítmény-mérők (fps, reprojekció, GPU ms, VRAM), grafikon,
  **CPU** (GetSystemTimes), **memória** (GlobalMemoryStatusEx), és minden óra-gomb külön.
- **Modern = Orbit-saját** (Erik: ne legyen egyértelmű Apple-másolat): a Look fül akcentszíne + a logó indigója
  (téma-váltással együtt változik), az értékek „pályák”: vékony teljes kör, az érték íve, a végén keringő
  „műhold”-pötty (`Orbit`, `Painter.Arc` kerek végekkel), határértéknél az Orbit Warn/Bad színe; analóg óra:
  pötty-jelölések, a másodperc egy a peremen keringő akcentpötty; feliratok szürke nagybetűvel, mint a beállításokban;
  gombok az Orbit panelszínében kerettel, bekapcsolva akcent. Mozgás: `Ui.Spring` (rugó al-lépésekkel; `snap`: a kis
  remegés nem animál, így az fps nem rajzoltat folyamatosan), a widgetek soronként beúsznak, amikor ránézel
  (`Intro`), a szerkesztőben új helyre rugóznak, új widget „beugrik”, az óra percei / az időzítő / a VR-idő
  számjegyei gördülnek (`RollText`). Mozgás közben legfeljebb 45 fps (`_lastDraw`); egy rajz ~2 ms (Debug), a
  beúszás ~1,25 s. A 95-ös stílusban nincs mozgás (a rugó ott azonnal célba ér).
- **Szerkesztő** (`Ui/SettingsWatchEditor.cs`, Wrist → Custom → Edit layout; 95: „Edit layout...”): élő előnézet,
  kattintás = kijelölés (referencia szerint, nem sorszám), húzás = mozgatás, sarok húzása = méret (a megfogáshoz
  képest), üres cella = ide jön a következő; paletta (Modern 5×5 gomb, 95 lista + Add), Smaller/Larger/Remove,
  háttér, „Start over from” (Full/Analog/Minimal/Time only/Corners), Empty.
- **Kinyitható widgetek** (Erik: „ha rálépek… megnagyobbodik a többi előterében és több információ vagy kontroll”;
  `Panels/WristDetail.cs`): a kártyára bökés kinyitja (`CardTap`, a widget saját gombjai előbb kapják a bökést),
  Modernben rugóval az egész rácsra nő egy sötétített óra előtt (`Ui.Inert`: mögötte minden csak kép), mellé bökve
  vagy ×-szel visszaáll; 95-ben a korabeli ablak-„zoom” körvonal után egy 95-ös ablak (X / OK zárja, mellé bökés
  nem). Nézetek: most szól (nagy borító, vezérlők, hangerő-csúszka), akkuk, idő (óra + dátum + hét + VR-idő; 95:
  Date/Time Properties), naptár (hónapok lapozva), időzítő (1/3/5/10/15/30 perc), stopper (köridők), teljesítmény
  (grafikon + átlag/legrosszabb/eldobott), rendszer (CPU-előzmény 60 s), hangerő. Az 1×1-es időzítő/stopper/hangerő
  bökésre most kinyílik (a vezérlés a kinyitott nézetben). Ha elfordítod a csuklód, bezárul.
- Az óra már nem rajzol újra minden lézermozdulatra (`NeedsRedrawForMove`), és a képkocka-mintavétel csak akkor
  fut, ha valami mutatja (`WristPanel.WantsFrames`; a widget-grafikon eddig üres maradt volna).
- Két ellenőrző kör (review workflow + cáfoló ellenőrök) javításai benne vannak.

## 1.4.0 – Windows 95 stílus (2026-10-14)

Kiadva: v1.4.0 (GitHub, OrbitSetup.exe sha256 8eccb33b…d954), és Erik dist-jébe a frissítő úton telepítve
(`OrbitSetup --update --install-dir dist`, config mentés: `config.json.pre-1.4-20261014-140709`).

Erik kérése: választható Win95-ös kinézet „mint a Mixtape-nél”; az első, csak átszínezett változatra:
„ez még nem az igazi, lehetne igazán autentikus, a stílus, a betűtípus, a layoutok, minden”. Mintának
a Regional Settings Properties lapot és egy Date/Time órát küldte.

- **Elv**: Win95 módban minden kis, valódi 1995-ös méretű vásznon készül (`Painter.BeginPixel`: aliasolt
  alakzatok, aliasolt monokróm szöveg, GDI-classic mérés, Microsoft Sans Serif 11 px = 8 pt), utána
  `ImageNearest` egész szorzóval nagyítja. Vásznak: beállítások 750×480 (VR-panel és dashboard ×2 =
  1500×960), óra 320×220 ×2, billentyűzet 740×338 ×2, értesítés 480×320 ×2.
- **Vezérlők** (`Ui/Ui95.cs`): Button95 (alapértelmezett fekete keret, benyomva, „latched” raszterezve),
  Check95, Radio95, Combo95 (+ `Popups95`: a nyitott lista mindenek fölött, alatta minden vezérlő vak; a
  listán kívüli kattintás CSAK bezár), Track95, Tabs95 (a kijelölt fül egybeolvad a lappal), List95
  görgetősávval, Group95, Edit95, Progress95 (blokkok), Pane95, Caption95 + ? / X / _ / □ gombok.
  Gyorsbillentyű-aláhúzás: `Label95("&Size:")`; adat (ablakcím, dal) `Text95`-tel megy, így a `&` nem tűnik el.
- **Beállítások** (`Ui/SettingsView95.cs`): „Orbit Properties” 7 füllel; csoportkeretek, „Címke: [vezérlő]”
  sorok, lenyíló listák (Attached to, Hand, Face, Laser on, Smoothing, Style...). Lent balra Recenter /
  Show keyboard / Show desktops, jobbra **OK / Close / Apply**: minden azonnal érvényes (mint egy 95-ös
  lap PSM_CANCELTOCLOSE után, ezért „Close” és nem visszavonni nem tudó Cancel); az Apply addig aktív,
  amíg a változás nincs lemezen (`ConfigStore.Dirty`), megnyomva azonnal ment. A ? a General fülre visz.
- **Asztali ablak** Win95 módban keret nélküli, maga a 95-ös ablak: a kék címsor húzza
  (WM_NCHITTEST → HTCAPTION, a ? és X fölött nem), szögletes sarok (DWMWCP_DONOTROUND), méret 750×480 × k
  (k = DPI kerekítve: 100–125 % → 1, vagyis 1080p-n valódi 1995-ös méret). Stílusváltáskor átáll oda-vissza.
- **Óra** (`Panels/WristPanel95.cs`): „Date/Time” ablak – nagy idő másodperccel, hosszú dátum a Windows
  területi beállítása szerint, analóg számlap pöttyökkel és domború türkiz mutatókkal (`Painter.Polygon`),
  CD Player-sor (borító, cím, |<< > >>|), Power Meter-akkuk, eszköztár (bekapcsolt gomb raszterezve),
  állapotsor vagy fekete-zöld grafikon. A _ és X elrejti, amíg el nem fordítod a csuklód (mindig látható
  módban 8 mp-re); a □ szürke. Az ablak magassága a bekapcsolt részekhez igazodik.
- **Billentyűzet** (`Panels/KeyboardPanel95.cs`): 95-ös gombok, nyomva benyomódik, rögzített módosító
  raszteres, egyszeri módosító fekete keretes; pixel-nyilak (←, ↵, kurzornyilak); a találati téglalapok
  nem változtak. **Értesítés**: 95-ös ablak navy címsorral és az Info ikonnal.
- **Ellenőrizve**: a Modern az 1.3.3-mal azonos (csatornánkénti snapshot-diff), kivéve a szándékos új
  Style-kapcsolót a Look fül alján és a verziószámot (az óra számjegyei az idő miatt térnek el). CSAPDA: a
  Pillow `getbbox()` RGBA képen csak az alfát nézi, átlátszatlan képeknél színeltérésre is None-t ad –
  csatornánként kell összevetni; `--snapshot`: lista-választás (Smoothing → High) és a listán kívüli kattintás (a Haptic ticks
  nem vált); `--wintest`: keret nélküli ablak, HTCAPTION a címsoron, 750×480 96 DPI-n, váltás oda-vissza.
- **Kiadás előtti átnézés** (4 szempont + 4 cáfoló ellenőr): 12 megerősített, mind javítva – egy 95-ben nyitva
  hagyott lenyíló lista Modernre váltás után is elnyelte a kattintásokat (`BeginPopups95` Modernben eldobja);
  ablakválasztó a sor helyett az ablak handle-jét jegyzi meg; a „Display:” választás nem jelöl változást;
  induláskor nincs Win11-keretes villanás (`OnLoad` → `ApplyStyle`), stílusváltáskor az ablak a képernyőn
  marad; a félkövér dalcím félkövérként mérve; a 95-ös billentyűzet felső gombjai is benyomódnak; a Modern
  szöveg ismét Grayscale (ahogy a `Gpu` beállítja, nem Default); Volume elengedésekor hangminta.

## Fejlesztői kapcsolók

- `--snapshot <mappa>` – VR nélkül PNG-be rajzol minden panelt + egy-egy képet minden monitorról (saját,
  külön config-gal, a valódit nem érinti).
- `--selftest` – futó SteamVR mellett: teszt-overlay, UV-irány ellenőrzés, frame sync, statisztika.
- `--scene [--dash]` – 12 másodpercig „játéknak” adja ki magát (bezárja a dashboardot); `--dash`: kinyitja az Orbit fület.
- `--osctest <port>` – egy-egy minta OSC-üzenet a megadott portra (formátum-ellenőrzéshez).
- `--tasktest` – ideiglenes, NEM admin feladattal kipróbálja a feladatos indítást, majd törli.
- `--filtertest` – a lézer-simítás remegése / késése szintenként.
- `--raytest` – a saját lézer-találatszámítás a futó SteamVR-rel szemben (alpha 0 tesztpanel, pár mp).
- `--leaktest <gpu|media|idle> [mp]` – egy háttér-összetevő privát memóriája egyedül futtatva, a végén GC-vel.
- `--wintest <mappa>` – az asztali beállításablak mindkét stílusban (saját config a mappában): kép, méret,
  és mit felel a 95-ös címsor a WM_NCHITTEST-re.
- `--mixertest` – a kimeneti eszköz appjai, ahogy a keverő olvassa (csak olvas, nem állít semmit).
- `--focustest` – a szövegmező-felismerés egy képernyőn kívüli tesztablakon (szövegmező, csak olvasható mező,
  gomb, RichEdit), közvetlenül és a fókusz-hookon át.

## Teljesítmény-napló

Az `orbit.log` 10 másodpercenként két sort ír:
- `perf:` képkocka/s, munka képkockánként, szakaszonként átlag/max ms (vr, lasers, wrist, kbd, settings,
  desktops, dash+osc);
- `mem:` a fő szál és az összes szál szemét-termelése (KB/s), GC-k száma gen0/1/2 és a szünetük, heap,
  working set, private bytes, handle-ök, GDI / USER objektumok, és szakaszonként a képkockánkénti bájtok.
  Ha volt GC, egy `gc:` sor is jön (generáció, háttér/tömörítő, szünet, előléptetett KB).

Mért és javított (1.2.0, 2026-10-13): a fő szál 300–550 KB/s szemetet termelt, emiatt 10 mp-enként futott
egy teljes GC 3–5 ms szünettel. A legnagyobb forrás a monitorképek átvétele volt a C#/WinRT-vetületen át
(1,3–3,9 KB/képkocka) – most nyers COM-hívásokkal megy (`MonitorCapture.Poll`, frame pool vtable [7],
frame [6]/[8], IClosable), szemét nélkül. Szintén javítva: `PlaceMissing` LINQ-listái minden képkockán
(264 B), a widget-azonosítók összefűzése (`Ui.K` / `Ui.Id` gyorsítótár), a lézer `foreach`-e az interfészen,
a kurzorkép 3×16 KB-os tömbjei. A VR-beállításpanel csak akkor rajzol újra, ha a mutató másik widgetre
ér (654 mozgásból 2–4 újrarajzolás), és induláskor minden fülét egyszer előrajzolja (első megnyitás
akadás nélkül). A fő ciklus szála AboveNormal prioritású.

Reszponzivitás (1.2.0, 2026-10-13 este):
- **Saját lézer-találatszámítás** (`Vr\LocalRaycast.cs`): a SteamVR `ComputeOverlayIntersection` IPC-hívása
  helyett (panelenként, kezenként, képkockánként; ez okozta a mért 5–16 ms-os akadásokat) folyamaton belül.
  `--raytest` ellenőrzi a futó SteamVR-rel szemben: egy alpha 0 tesztpanel 156 alakban, 62 400 sugár →
  **0 eltérés** (max uv 0,00015, távolság 0,04 mm). Közben kimérve: SteamVR a görbült panelt csak ott
  találja el, ahol a sugár KILÉP a hengerből (homorú oldal; hátulról soha), a sík panelt mindkét oldalról;
  rejtett panelre nem ad találatot; panel-változást csak egy későbbi képkockán alkalmaz; textúra nélkül
  uv = 0; 0,0005 alatti görbületnél maga is pontatlan (float) – az Orbit ezt síkra kerekíti
  (`DesktopLayout.MinCurvature`). Mód: `General.Raycast` = `shadow` (SteamVR dönt, a saját is lefut, a
  naplóba `raycast:` sor jön az eltérésekkel és a hívásonkénti µs-mal) | `local` | `steamvr`.
- **Képrögzítés ütemezése**: pontos átlag-ütemezés (`DesktopPanel.CaptureDue`) – a régi „most + 1/fps” a
  ~80–90 Hz-es ciklusban minden második ciklust kihagyta (60 fps-ből ~40 lett). Okos módban a nézett / mutatott
  monitor és ablak a headset frissítésével megy (90 Hz; a monitor saját frissítése és a ciklus a felső határ).
- **GPU-prioritás**: `IDXGIDevice.SetGPUThreadPriority(7)` (admin kell; `General.HighGpuPriority`), és minden
  képkocka után egy esemény-lekérdezés + Flush: a `perf:` sor `gpu late %` értéke mutatja, hány képkockánál
  nem végzett még az Orbit GPU-munkája a következő ciklusra.
- **Energiatakarékos ütemezés kikapcsolva**: `SetProcessInformation(ProcessPowerThrottling)` – EXECUTION_SPEED
  és IGNORE_TIMER_RESOLUTION nélkül (a Windows 11 a háttérben futó, ablak nélküli folyamatokat lassítaná).
- **Médiafigyelő**: másodpercenkénti lekérdezés helyett a Windows változás-eseményeire olvas (legfeljebb 2 mp-enként,
  parancs után azonnal), 20 olvasásonként egy gen0 GC. Mérve (`--leaktest media`): a régi 1 mp-es lekérdezés
  ~14 MB/perc natív memóriát hagyott a WinRT-objektumok mögött (csak GC szabadítja fel), az új stabil.
  `--leaktest gpu`: a GPU-számlálók nem szivárognak.

## Tesztelve / nem tesztelve (2026-10-13)

Tesztelve SteamVR null headsettel (headset nélkül): overlay-ek létrejönnek, a 3 monitor élőben megy
és ívben áll, billentyűzet, dashboard-fül, UV-irány (bal alsó sarok → tükrözni kell), színek.
Szintén null headsettel (2. kör): a görbült overlay geometriája mérve (tengely r-re a panel előtt, a szélesség
= ívhossz), a 3 monitor hézagmentes íves sorba áll; OSC-üzenetek formátuma UDP-n visszaolvasva.
**Kontroller nélkül nem tesztelhető**: lézer, kattintás, megfogás, csukló-óra helye és előtűnése,
dupla-grip kapcsoló, összepattintás ejtéssel, kétkezes átméretezés. DXOverlay élesben nincs kipróbálva.
Ha az óra nem jó helyen van, a másik kézzel megfogva odébb tehető; a Wrist → Reset position visszaállítja.
1.1.0 (2026-10-13): ablak-capture élesben mérve (`--snapshot`): a kép mérete pontosan a DWM látható
ablakkerete (1920×1032 = 1920×1032), erre épül az egér-leképezés. Számlapok, grafikon, Look/Windows fül
és a lekerekített panel snapshotban ellenőrizve. VR-ben (kontrollerrel) az ablakpanel kattintása,
az előtérbe hozás és a headset-levétel szünet még nincs kipróbálva.

## Rendszergazdaként (alapból bekapcsolva)

Admin joggal fut, hogy a VR-egér és -billentyűzet a rendszergazdaként futó ablakokba (Feladatkezelő,
Hexpad stb.) is be tudjon kattintani/gépelni – a Windows (UIPI) egy nem-admin programtól ezt letiltja.
Az **első** indításkor egyszer feljön a UAC; ekkor az Orbit létrehoz egy ütemezett feladatot
(**„Orbit (admin)”**, csak kézi indítású, nincs időzítése). Ezután a parancsikonról és a SteamVR-ből is
kérdés nélkül, ezen a feladaton át indul admin joggal. General → *Run as administrator* kikapcsolva
nem kér admin jogot (admin példányból kikapcsolva a feladatot is törli); `Orbit.exe --no-elevate`
egyszer admin nélkül indít. Ha az exe máshova kerül, a következő indításkor újra kér egy UAC-ot,
és frissíti a feladatot.

## Ismert korlátok

- A UAC-ablakra magára (biztonsági asztal) VR-ből sem lehet kattintani – ez minden overlaynél így van.
- A csuklón nincs GPU-terhelés %: a Windows GPU-számlálói az AMD kártyán erősen alulmérnek
  (a Hexpadnél mérve 2 % valódi 99 % mellett). A VRAM és a SteamVR GPU ms pontos.
- A monitorra kattintás a valódi egérrel kattint, tehát elveszi a fókuszt a VRChat ablakától
  (a VRChat VR-ben ettől fut tovább).
- Tükrözött ablak: amíg csak mutatsz rá (nem kattintasz), a valódi kurzor a PC-n az ablak helyére megy;
  ha azt az ablakot ott egy másik takarja, a hover-effektek a takaró ablakon látszanak. Az első
  kattintás/görgetés előtérbe hozza. Kis méretre tett ablakról nem jön új kép (az utolsó marad).

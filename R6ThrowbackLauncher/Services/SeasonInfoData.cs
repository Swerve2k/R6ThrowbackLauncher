using System.Collections.Generic;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public static class SeasonInfoData
    {
        public static SeasonInfo? For(string code) => Table.TryGetValue(code, out var info) ? info : null;

        public static readonly SeasonInfo HeatedMetal = new()
        {
            Highlights = new[]
            {
                "Community mod built on top of this build by DataCluster0",
                "Adds custom game modes, scripting and an in-game map editor",
                "Unlocks all operators and cosmetics locally",
            },
        };

        public static IReadOnlyList<string> Requirements(SeasonPatch patch)
        {
            var list = new List<string> { "Latest Visual C++ Redistributable (x64)" };
            if (patch.IsHeatedMetal)
            {
                list.Add("Disable third-party overlays such as Overwolf");
            }
            else if (patch.Rank >= 84)
            {
                list.Add("Most operators are locked in this build. Striker and Sentry become available after the tutorial.");
            }
            return list;
        }

        private static InfoOperator Op(string name, string gadget, string summary) => new(name, gadget, summary);

        private static readonly Dictionary<string, SeasonInfo> Table = new()
        {
            ["Y1S0"] = new()
            {
                Release = "Dec 1, 2015",
                Highlights = new[]
                {
                    "Launch roster of 20 operators from SAS, FBI SWAT, GIGN, Spetsnaz and GSG 9",
                    "Launch map pool including House, Bank, Clubhouse, Consulate, Kafe Dostoyevsky and Chalet",
                },
            },
            ["Y1S1"] = new()
            {
                Release = "Feb 2, 2016",
                Operators = new[]
                {
                    Op("Buck", "Skeleton Key", "Under-barrel shotgun for opening floors and soft walls"),
                    Op("Frost", "Welcome Mat", "Folding leg traps that down attackers who step in them"),
                },
                Maps = new[] { "Yacht" },
                Highlights = new[] { "The Canadian JTF2 unit joins the roster", "Yacht: a superyacht locked in Arctic ice" },
            },
            ["Y1S2"] = new()
            {
                Release = "May 10, 2016",
                Operators = new[]
                {
                    Op("Blackbeard", "TARS Rifle Shield", "Bulletproof visor mounted on his rifle"),
                    Op("Valkyrie", "Black Eye", "Throwable sticky cameras for extra intel"),
                },
                Maps = new[] { "Border" },
                Highlights = new[] { "The U.S. Navy SEAL unit joins the roster", "Border: a checkpoint on a desert frontier" },
            },
            ["Y1S3"] = new()
            {
                Release = "Aug 2, 2016",
                Operators = new[]
                {
                    Op("Capitão", "Tactical Crossbow", "Fires asphyxiating bolts and micro-smoke bolts"),
                    Op("Caveira", "Silent Step", "Near-silent movement and the Interrogation finisher"),
                },
                Maps = new[] { "Favela" },
                Highlights = new[] { "The Brazilian BOPE unit joins the roster", "Tactical Realism playlist with a stripped-down HUD" },
            },
            ["Y1S4"] = new()
            {
                Release = "Nov 17, 2016",
                Operators = new[]
                {
                    Op("Hibana", "X-KAIROS", "Pellet launcher that opens reinforced walls and hatches"),
                    Op("Echo", "Yokai", "Ceiling-mounted drone that fires disorienting sonic bursts"),
                },
                Maps = new[] { "Skyscraper" },
                Highlights = new[] { "The Japanese SAT unit joins the roster", "Skyscraper: a penthouse and traditional garden in Japan" },
            },
            ["Y2S1"] = new()
            {
                Release = "Feb 7, 2017",
                Operators = new[]
                {
                    Op("Jackal", "Eyenox Model III", "Reveals recent footprints and tracks defenders"),
                    Op("Mira", "Black Mirror", "One-way bulletproof windows set into walls"),
                },
                Maps = new[] { "Coastline" },
                Highlights = new[] { "The Spanish GEO unit joins the roster", "Coastline: a beach club on Ibiza" },
            },
            ["Y2S2"] = new()
            {
                Release = "Jun 7, 2017",
                Highlights = new[]
                {
                    "A season dedicated to bug fixes and stability instead of new content",
                    "Groundwork for the netcode and matchmaking improvements that followed",
                },
            },
            ["Y2S3"] = new()
            {
                Release = "Sep 5, 2017",
                Operators = new[]
                {
                    Op("Ying", "Candela", "Cluster charges that blind everyone nearby"),
                    Op("Lesion", "Gu Mine", "Cloaked mines that poison and slow attackers"),
                    Op("Ela", "Grzmot Mine", "Sticky mines that release a concussive blast"),
                },
                Maps = new[] { "Theme Park" },
                Highlights = new[] { "The Hong Kong SDU and Polish GROM units join", "Three operators in a single season" },
            },
            ["Y2S4"] = new()
            {
                Release = "Dec 5, 2017",
                Operators = new[]
                {
                    Op("Dokkaebi", "Logic Bomb", "Rings every defender's phone and hacks their cameras"),
                    Op("Vigil", "ERC-7", "Erases his image from cameras and drones"),
                    Op("Zofia", "KS79 Lifeline", "Launches impact and concussion grenades"),
                },
                Maps = new[] { "Tower" },
                Highlights = new[] { "The South Korean 707th SMB joins the roster", "Tower: a high-rise in Seoul" },
            },
            ["Y3S1"] = new()
            {
                Release = "Mar 6, 2018",
                Operators = new[]
                {
                    Op("Lion", "EE-ONE-D", "Drone scan that reveals any defender who moves"),
                    Op("Finka", "Adrenal Surge", "Boosts her team's health and steadies their aim"),
                },
                Highlights = new[] { "The CBRN Threat Unit debuts", "Outbreak co-op event in Truth or Consequences, New Mexico" },
                Events = new[] { "Outbreak" },
            },
            ["Y3S2"] = new()
            {
                Release = "Jun 7, 2018",
                Operators = new[]
                {
                    Op("Alibi", "Prisma", "Hologram decoys that ping attackers who shoot them"),
                    Op("Maestro", "Evil Eye", "Bulletproof camera turret that fires a laser"),
                },
                Maps = new[] { "Villa" },
                Highlights = new[] { "The Italian GIS unit joins the roster", "Villa: an estate in the Italian countryside" },
            },
            ["Y3S3"] = new()
            {
                Release = "Sep 4, 2018",
                Operators = new[]
                {
                    Op("Maverick", "Breaching Torch", "Blowtorch that silently cuts through reinforced walls"),
                    Op("Clash", "CCE Shield", "Extendable shield that slows attackers with electric shocks"),
                },
                Maps = new[] { "Hereford Base" },
                Highlights = new[] { "First full map rework: Hereford Base", "Clash is the first defender with a shield" },
                Events = new[] { "Mad House" },
            },
            ["Y3S4"] = new()
            {
                Release = "Dec 4, 2018",
                Operators = new[]
                {
                    Op("Nomad", "Airjab Launcher", "Proximity grenades that knock defenders off their feet"),
                    Op("Kaid", "Rtila Electroclaw", "Electrifies reinforced walls and hatches from a distance"),
                },
                Maps = new[] { "Fortress" },
                Highlights = new[] { "The Moroccan GIGR unit joins the roster", "Fortress: a kasbah in the desert" },
            },
            ["Y4S1"] = new()
            {
                Release = "Mar 6, 2019",
                Operators = new[]
                {
                    Op("Gridlock", "Trax Stingers", "Spreads spiked mats that slow and damage defenders"),
                    Op("Mozzie", "Pest Launcher", "Hijacks attacker drones for the defense"),
                },
                Maps = new[] { "Outback" },
                Highlights = new[] { "The Australian SASR unit joins the roster", "Pick and ban phase added to ranked" },
                Events = new[] { "Rainbow is Magic" },
            },
            ["Y4S2"] = new()
            {
                Release = "Jun 11, 2019",
                Operators = new[]
                {
                    Op("Nøkk", "HEL Presence Reduction", "Hides her from cameras and drones"),
                    Op("Warden", "Glance Smart Glasses", "Sees through smoke and shrugs off flashes"),
                },
                Maps = new[] { "Kafe Dostoyevsky" },
                Highlights = new[] { "Kafe Dostoyevsky rework" },
                Events = new[] { "Showdown" },
            },
            ["Y4S3"] = new()
            {
                Release = "Sep 11, 2019",
                Operators = new[]
                {
                    Op("Amaru", "Garra Hook", "Grappling hook into windows and hatches"),
                    Op("Goyo", "Volcán Shield", "Deployable shield hiding an incendiary canister"),
                },
                Maps = new[] { "Kanal" },
                Highlights = new[] { "Kanal rework", "Champion rank added at the top of ranked" },
                Events = new[] { "Doktor's Curse", "Money Heist" },
            },
            ["Y4S4"] = new()
            {
                Release = "Dec 3, 2019",
                Operators = new[]
                {
                    Op("Kali", "LV Explosive Lance", "Rifle-mounted lance that destroys gadgets through walls"),
                    Op("Wamai", "Mag-NET System", "Catches thrown projectiles and redirects them"),
                },
                Maps = new[] { "Theme Park" },
                Highlights = new[] { "Theme Park rework", "The Nighthaven faction is introduced" },
                Events = new[] { "Road to S.I. 2020" },
            },
            ["Y5S1"] = new()
            {
                Release = "Mar 10, 2020",
                Operators = new[]
                {
                    Op("Iana", "Gemini Replicator", "Remote-controlled hologram of herself for scouting"),
                    Op("Oryx", "Remah Dash", "Charges through soft walls and climbs through hatches"),
                },
                Maps = new[] { "Oregon" },
                Highlights = new[] { "Oregon rework" },
                Events = new[] { "Grand Larceny", "Golden Gun" },
            },
            ["Y5S2"] = new()
            {
                Release = "Jun 16, 2020",
                Operators = new[]
                {
                    Op("Ace", "S.E.L.M.A. Aqua Breacher", "Hydraulic charges that cut repeated holes in reinforcements"),
                    Op("Melusi", "Banshee Sonic Defense", "Bulletproof device that slows nearby attackers"),
                },
                Maps = new[] { "House" },
                Highlights = new[] { "House rework" },
                Events = new[] { "M.U.T.E. Protocol" },
            },
            ["Y5S3"] = new()
            {
                Release = "Sep 10, 2020",
                Operators = new[]
                {
                    Op("Zero", "ARGUS Launcher", "Fires cameras that pierce walls and shoot a laser"),
                    Op("Tachanka", "Shumikha Launcher", "Rework: an incendiary grenade launcher"),
                },
                Maps = new[] { "Chalet" },
                Highlights = new[] { "Chalet rework", "Weapon sights overhaul with new optics" },
                Events = new[] { "Golden Gun", "Sugar Fright" },
            },
            ["Y5S4"] = new()
            {
                Release = "Dec 1, 2020",
                Operators = new[]
                {
                    Op("Aruni", "Surya Gate", "Laser gates that damage anyone who passes through"),
                },
                Maps = new[] { "Skyscraper" },
                Highlights = new[] { "Skyscraper rework", "Hard Breach Charge added as a secondary gadget" },
                Events = new[] { "Road to S.I. 2021" },
            },
            ["Y6S1"] = new()
            {
                Release = "Mar 16, 2021",
                Operators = new[] { Op("Flores", "RCE-Ratero Charge", "Remote-driven explosive drones") },
                Maps = new[] { "Border" },
                Highlights = new[] { "Border rework", "Gonne-6 secondary gadget added" },
                Events = new[] { "Rainbow is Magic", "Apocalypse" },
            },
            ["Y6S2"] = new()
            {
                Release = "Jun 14, 2021",
                Operators = new[] { Op("Thunderbird", "Kóna Station", "Healing stations that can revive downed teammates") },
                Maps = new[] { "Favela" },
                Highlights = new[] { "Favela rework" },
                Events = new[] { "Containment" },
            },
            ["Y6S3"] = new()
            {
                Release = "Sep 7, 2021",
                Operators = new[] { Op("Osa", "Talon-8 Clear Shield", "Transparent shields for windows and doorways") },
                Maps = new[] { "Bank", "Coastline", "Clubhouse" },
                Highlights = new[] { "Map updates for Bank, Coastline and Clubhouse" },
                Events = new[] { "Showdown", "Doktor's Curse" },
            },
            ["Y6S4"] = new()
            {
                Release = "Nov 30, 2021",
                Operators = new[] { Op("Thorn", "Razorbloom Shell", "Proximity bomb that detonates after a short delay") },
                Maps = new[] { "Outback" },
                Highlights = new[] { "Outback rework", "Shooting Range added" },
                Events = new[] { "Snow Brawl", "Road to S.I. 2022" },
            },
            ["Y7S1"] = new()
            {
                Release = "Mar 15, 2022",
                Operators = new[]
                {
                    Op("Azami", "Kiba Barrier", "Kunai that expand into bulletproof barriers"),
                    Op("Goyo", "Volcán Canister", "Rework: throwable incendiary canisters"),
                },
                Maps = new[] { "Emerald Plains" },
                Highlights = new[] { "Emerald Plains: a country club in Ireland", "Reputation system introduced" },
                Events = new[] { "Rengoku" },
            },
            ["Y7S2"] = new()
            {
                Release = "Jun 14, 2022",
                Operators = new[] { Op("Sens", "R.O.U. Projector System", "Rolls out a wall of light that blocks line of sight") },
                Maps = new[] { "Close Quarter" },
                Highlights = new[] { "Team Deathmatch playlist", "Close Quarter: a compact map built for Team Deathmatch" },
                Events = new[] { "M.U.T.E. Protocol" },
            },
            ["Y7S3"] = new()
            {
                Release = "Sep 6, 2022",
                Operators = new[] { Op("Grim", "Kawan Hive Launcher", "Releases a swarm that pings defenders caught in it") },
                Maps = new[] { "Stadium Bravo" },
                Highlights = new[] { "Stadium Bravo: a limited-time map remixing classic locations" },
                Events = new[] { "Snipers", "Doktor's Curse" },
            },
            ["Y7S4"] = new()
            {
                Release = "Dec 6, 2022",
                Operators = new[] { Op("Solis", "SPEC-IO Electro-Sensor", "Scans for and highlights electronic gadgets") },
                Maps = new[] { "Nighthaven Labs" },
                Highlights = new[] { "Ranked 2.0 overhaul", "Cross-play and cross-progression" },
                Events = new[] { "Snow Brawl" },
            },
            ["Y8S1"] = new()
            {
                Release = "Mar 7, 2023",
                Operators = new[] { Op("Brava", "Kludge Drone", "Drone that hijacks defender gadgets") },
                Highlights = new[] { "Attackers can repick operators during preparation" },
                Events = new[] { "Rainbow is Magic" },
            },
            ["Y8S2"] = new()
            {
                Release = "May 30, 2023",
                Operators = new[] { Op("Fenrir", "F-NATT Dread Mine", "Mines that cloud attackers' vision with fear gas") },
                Maps = new[] { "Consulate" },
                Highlights = new[] { "Consulate rework" },
                Events = new[] { "Rengoku" },
            },
            ["Y8S3"] = new()
            {
                Release = "Aug 29, 2023",
                Operators = new[]
                {
                    Op("Ram", "BU-GI Auto-Breacher", "Armored drone that breaks floors and soft walls"),
                    Op("Frost", "Welcome Mat", "Rework of her leg traps"),
                },
                Highlights = new[] { "Frost rework" },
                Events = new[] { "Doktor's Curse" },
            },
            ["Y8S4"] = new()
            {
                Release = "Dec 6, 2023",
                Operators = new[] { Op("Tubarão", "Zoto Canister", "Freezes nearby gadgets in place") },
                Maps = new[] { "Lair" },
                Highlights = new[] { "Lair map added" },
                Events = new[] { "Freeze For All" },
            },
            ["Y9S1"] = new()
            {
                Release = "Mar 12, 2024",
                Operators = new[] { Op("Deimos", "DeathMARK Tracker", "Locks onto a defender and tracks their position") },
                Events = new[] { "Containment" },
            },
            ["Y9S2"] = new()
            {
                Release = "Jun 11, 2024",
                Operators = new[]
                {
                    Op("Striker", "Gadget Kit", "Attacking recruit with a flexible loadout"),
                    Op("Sentry", "Gadget Kit", "Defending recruit with a flexible loadout"),
                },
                Highlights = new[] { "Recruit split into Striker and Sentry", "Reworked onboarding for new players" },
                Events = new[] { "M.U.T.E. Protocol" },
            },
            ["Y9S3"] = new()
            {
                Release = "Sep 10, 2024",
                Operators = new[] { Op("Skopós", "V10 Pantheon Shells", "Controls two robotic bodies and swaps between them") },
                Events = new[] { "Doktor's Curse" },
            },
            ["Y9S4"] = new()
            {
                Release = "Dec 3, 2024",
                Operators = new[] { Op("Blackbeard", "H.U.L.L. Adaptable Shield", "Rework: a deployable cover shield") },
                Events = new[] { "Freeze For All", "Assault on Hereford" },
            },
            ["Y10S1"] = new()
            {
                Release = "Mar 4, 2025",
                Operators = new[] { Op("Rauora", "D.O.M. Panel Launcher", "Launches panels that block doorways") },
            },
            ["Y10S2"] = new()
            {
                Release = "Jun 10, 2025",
                Operators = new[] { Op("Clash", "CCE Shield MK2", "Rework of her extendable shield") },
                Maps = new[] { "District" },
                Highlights = new[] { "Siege X relaunch", "District map" },
                Events = new[] { "Showdown", "Rengoku" },
            },
            ["Y10S3"] = new()
            {
                Release = "Sep 2, 2025",
                Operators = new[] { Op("Denari", "T.R.I.P. Connector", "Links devices with tripwire lasers") },
                Events = new[] { "M.U.T.E. Protocol", "Doktor's Curse" },
            },
            ["Y10S4"] = new()
            {
                Release = "Dec 2, 2025",
                Operators = new[] { Op("Thatcher", "E.G.S. Disruptor", "Rework of his anti-electronics gadget") },
                Maps = new[] { "Fortress" },
                Events = new[] { "Freeze For All" },
            },
            ["Y11S1"] = new()
            {
                Release = "Mar 3, 2026",
                Operators = new[] { Op("Solid Snake", "Soliton Radar MKIII", "Radar that reveals nearby enemies") },
                Events = new[] { "Rainbow is Magic" },
            },
            ["Y11S2"] = new()
            {
                Release = "Jun 2, 2026",
                Operators = new[] { Op("Dokkaebi", "Jegeo Payload", "Rework of her hacking gadget") },
                Maps = new[] { "Calypso Casino" },
                Events = new[] { "Rengoku" },
            },
        };
    }
}

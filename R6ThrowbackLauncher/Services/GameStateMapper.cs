using System.Collections.Generic;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>
    /// Converts raw memory values (UIDs, IDs) into human-readable strings.
    ///
    /// ── HOW TO CONFIRM A UID / ID ──────────────────────────────────────────
    ///   1. Run the game, open the Output window in Visual Studio (debug build).
    ///   2. GameMemoryService logs: "Map UID raw = 0x{value}".
    ///   3. Note which map you're on → add the entry to MapNames below.
    ///   4. Repeat for each map / operator / gamemode.
    ///
    /// ── New Blood (Situations) maps to test on ─────────────────────────────
    ///   Situation 1 — Lone Wolf       (Hereford Base)
    ///   Situation 2 — Shut Down       (Hereford Base)
    ///   Situation 3 — Smash & Grab    (Hereford Base)
    ///   Situation 4 — Terrorist Hunt  (House)
    ///   ...
    /// ───────────────────────────────────────────────────────────────────────
    /// </summary>
    public static class GameStateMapper
    {
        // ── Map UID → Name ─────────────────────────────────────────────────────
        // Keys are the raw ulong read from memory.
        // Replace the placeholder keys (0xDEAD…) with real values observed via debug logs.
        private static readonly Dictionary<ulong, string> MapNames = new()
        {
            // ── Year 1 maps ──────────────────────────────────────────────
            { 0xDEADBEEF00000001, "House"              },
            { 0xDEADBEEF00000002, "Hereford Base"      },
            { 0xDEADBEEF00000003, "Chalet"             },
            { 0xDEADBEEF00000004, "Yacht"              },
            { 0xDEADBEEF00000005, "Skyscraper"         },
            { 0xDEADBEEF00000006, "Consulate"          },
            { 0xDEADBEEF00000007, "Bank"               },
            { 0xDEADBEEF00000008, "Kanal"              },
            { 0xDEADBEEF00000009, "Kafe Dostoyevsky"   },
            { 0xDEADBEEF0000000A, "Oregon"             },
            { 0xDEADBEEF0000000B, "Border"             },
            { 0xDEADBEEF0000000C, "Favela"             },
            { 0xDEADBEEF0000000D, "Presidential Plane" },
            // ── Year 2 maps ──────────────────────────────────────────────
            { 0xDEADBEEF00000010, "Coastline"          },
            { 0xDEADBEEF00000011, "Tower"              },
            { 0xDEADBEEF00000012, "Clubhouse"          },
            { 0xDEADBEEF00000013, "Bartlett U."        },
            // ── Year 3 maps ──────────────────────────────────────────────
            { 0xDEADBEEF00000020, "Fortress"           },
            { 0xDEADBEEF00000021, "Outback"            },
            { 0xDEADBEEF00000022, "Theme Park"         },
            { 0xDEADBEEF00000023, "Villa"              },
        };

        // ── Gamemode ID → Name ─────────────────────────────────────────────────
        private static readonly Dictionary<int, string> GamemodeNames = new()
        {
            { 0, "Bomb"         },
            { 1, "Secure Area"  },
            { 2, "Hostage"      },
            { 3, "Situations"   },
            { 4, "Terrorist Hunt" },
        };

        // ── Operator ID → Name ─────────────────────────────────────────────────
        // IDs below are placeholders — fill in from debug log observations.
        private static readonly Dictionary<int, string> OperatorNames = new()
        {
            // ── SAS ──────────────────────────────────────────────────────
            { 1,  "Sledge"     },
            { 2,  "Thatcher"   },
            { 3,  "Smoke"      },
            { 4,  "Mute"       },
            // ── FBI SWAT ─────────────────────────────────────────────────
            { 5,  "Ash"        },
            { 6,  "Thermite"   },
            { 7,  "Castle"     },
            { 8,  "Pulse"      },
            // ── GIGN ─────────────────────────────────────────────────────
            { 9,  "Twitch"     },
            { 10, "Montagne"   },
            { 11, "Doc"        },
            { 12, "Rook"       },
            // ── Spetsnaz ────────────────────────────────────────────────
            { 13, "Glaz"       },
            { 14, "Fuze"       },
            { 15, "Kapkan"     },
            { 16, "Tachanka"   },
            // ── GSG 9 ─────────────────────────────────────────────────
            { 17, "Blitz"      },
            { 18, "IQ"         },
            { 19, "Jager"      },
            { 20, "Bandit"     },
            // ── JTF-2 (Y2S1) ────────────────────────────────────────────
            { 21, "Buck"       },
            { 22, "Frost"      },
            // ── Navy SEALs (Y2S1) ────────────────────────────────────────
            { 23, "Blackbeard" },
            { 24, "Valkyrie"   },
            // ── BOPE (Y2S2) ─────────────────────────────────────────────
            { 25, "Caveira"    },
            { 26, "Capitao"    },
            // ── SAT (Y2S3) ──────────────────────────────────────────────
            { 27, "Hibana"     },
            { 28, "Echo"       },
            // ── GEO (Y2S4) ──────────────────────────────────────────────
            { 29, "Jackal"     },
            { 30, "Mira"       },
            // ── SDU (Y3S1) ──────────────────────────────────────────────
            { 31, "Ying"       },
            { 32, "Lesion"     },
            // ── GROM (Y3S2) ─────────────────────────────────────────────
            { 33, "Ela"        },
            { 34, "Zofia"      },
            // ── 707th SMB (Y3S3) ─────────────────────────────────────────
            { 35, "Dokkaebi"   },
            { 36, "Vigil"      },
            // ── CBRN (Y3S4) ─────────────────────────────────────────────
            { 37, "Lion"       },
            { 38, "Finka"      },
        };

        // ── Public lookup methods ──────────────────────────────────────────────

        /// <summary>Returns the map name for a UID, or null if unknown.</summary>
        public static string? MapName(ulong uid)
            => MapNames.TryGetValue(uid, out var name) ? name : null;

        /// <summary>Returns the gamemode name for an ID, or null if unknown.</summary>
        public static string? GamemodeName(int id)
            => GamemodeNames.TryGetValue(id, out var name) ? name : null;

        /// <summary>Returns the operator name for an ID, or null if unknown.</summary>
        public static string? OperatorName(int id)
            => OperatorNames.TryGetValue(id, out var name) ? name : null;

        /// <summary>
        /// Registers a newly discovered map UID at runtime (from debug observation).
        /// Useful while investigating offsets — call from debug code, then add to the dict above.
        /// </summary>
        public static void RegisterMapUid(ulong uid, string name)
        {
            if (!MapNames.ContainsKey(uid))
                MapNames[uid] = name;
        }
    }
}

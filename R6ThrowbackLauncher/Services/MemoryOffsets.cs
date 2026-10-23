namespace R6ThrowbackLauncher.Services
{
    /// <summary>
    /// All memory offsets for reading game state from RainbowSix.exe.
    ///
    /// ── HOW TO FIND THESE WITH CHEAT ENGINE ────────────────────────────────
    ///
    ///  MAP UID
    ///  1. Launch RainbowSix.exe, attach Cheat Engine.
    ///  2. While on a known map (e.g. New Blood / Hereford Base):
    ///     - Scan 8-byte value, "Unknown Initial Value"
    ///     - Load a different situation → Changed Value
    ///     - Load the same one again   → Unchanged Value
    ///     Repeat until 1-5 candidates remain.
    ///  3. Pointer-scan those candidates from the module base
    ///     (Tools → Pointer scan → "Pointer map").
    ///  4. Fill MapBase and MapPtrChain[] below.
    ///
    ///  GAMEMODE ID
    ///  Same approach, but scan 4-byte int while switching between
    ///  Bomb / Secure Area / Hostage.
    ///  Usually lives at a fixed offset inside the same MapObject,
    ///  so try MapObject + GamemodeOffset first.
    ///
    ///  OPERATOR ID
    ///  1. Pick an operator, scan 4-byte int.
    ///  2. Change operator, Changed Value.
    ///  3. Pointer-scan the result.
    ///  4. Fill OperatorBase and OperatorPtrChain[].
    ///
    ///  TIPS
    ///  - Always use pointer chains, never raw addresses — they shift each run.
    ///  - Test each offset by changing the value in-game and confirming it
    ///    updates. If it doesn't, the offset is wrong.
    ///  - Enable debug logging in GameMemoryService to see raw reads in the
    ///    Output window while investigating.
    /// ───────────────────────────────────────────────────────────────────────
    /// </summary>
    public static class MemoryOffsets
    {
        // ── Map UID ────────────────────────────────────────────────────────────
        // Pointer chain:
        //   [RainbowSix.exe + MapBase]  → deref  → add each MapPtrChain offset
        //   and deref again, until the last hop gives us the MapObject.
        //   Then read a ulong at MapUidOffset inside that object.
        //
        //   Example chain (3 hops):
        //     addr = *(ModuleBase + 0x04B3C120)
        //     addr = *(addr + 0x60)
        //     addr = *(addr + 0x10)
        //     mapUid = *(ulong*)(addr + 0x38)
        //
        /// <summary>False until real offsets are filled in; the reader stays off until then.</summary>
        public static bool IsConfigured => MapBase != 0 || OperatorBase != 0;

        public const  long   MapBase      = 0x00000000;          // TODO
        public static int[]  MapPtrChain  = { 0x00, 0x00 };      // TODO: intermediate hops
        public const  int    MapUidOffset = 0x00;                 // TODO: byte offset of the UID inside MapObject

        // ── Gamemode ───────────────────────────────────────────────────────────
        // Typically sits at a fixed offset from the same MapObject resolved above.
        //   gamemode = *(int*)(mapObject + GamemodeOffset)
        public const int GamemodeOffset = 0x00;                   // TODO

        // ── Operator ID ────────────────────────────────────────────────────────
        // Pointer chain:
        //   [RainbowSix.exe + OperatorBase] → deref → add each OperatorPtrChain
        //   offset and deref again → read int at OperatorIdOffset.
        public const  long   OperatorBase      = 0x00000000;      // TODO
        public static int[]  OperatorPtrChain  = { 0x00, 0x00 };  // TODO
        public const  int    OperatorIdOffset  = 0x00;             // TODO

        // ── Poll config ────────────────────────────────────────────────────────
        /// <summary>How often (ms) to re-read memory while the game is running.</summary>
        public const int PollIntervalMs = 2000;

        // ── Win32 constants ────────────────────────────────────────────────────
        public const uint PROCESS_VM_READ            = 0x0010;
        public const uint PROCESS_QUERY_INFORMATION  = 0x0400;
    }
}

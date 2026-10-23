using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    // ── Game state snapshot ───────────────────────────────────────────────────

    /// <summary>Clean, mapped snapshot of the current game state.</summary>
    public sealed record GameState(
        ulong  MapUid,
        int    GamemodeId,
        int    OperatorId,
        string? MapName,
        string? GamemodeName,
        string? OperatorName
    )
    {
        /// <summary>True if we have at least a map name.</summary>
        public bool HasMapData => MapName != null;

        /// <summary>Discord details line: "Oregon  •  Bomb" etc.</summary>
        public string DetailsLine =>
            (MapName, GamemodeName) switch
            {
                (string m, string g) => $"{m}  •  {g}",
                (string m, null    ) => m,
                (null,     string g) => g,
                _                   => "In Menus"
            };

        /// <summary>Discord state line: "Playing as Sledge" etc.</summary>
        public string? OperatorLine =>
            OperatorName != null ? $"Playing as {OperatorName}" : null;
    }

    // ── Service ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads game memory from a running RainbowSix.exe process and fires
    /// <see cref="StateChanged"/> whenever the map, gamemode, or operator changes.
    ///
    /// Architecture (per the design guide):
    ///   [Memory Layer]  — reads raw bytes via ReadProcessMemory
    ///   [Parser Layer]  — follows pointer chains, extracts ulong / int values
    ///   [Mapper Layer]  — calls GameStateMapper to convert IDs → strings
    ///   [Event Layer]   — fires StateChanged so the caller can push to Discord RPC
    ///
    /// Offsets are in <see cref="MemoryOffsets"/>. Set DEBUG_LOG = true to print
    /// raw values to the Output window while investigating with Cheat Engine.
    /// </summary>
    public sealed class GameMemoryService : IDisposable
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const bool DEBUG_LOG = true;   // ← set false in production

        // ── Win32 P/Invoke ────────────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress,
            byte[] lpBuffer, int nSize, out int lpBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // ── State ─────────────────────────────────────────────────────────────
        private readonly Process            _gameProcess;
        private          IntPtr             _handle       = IntPtr.Zero;
        private          IntPtr             _moduleBase   = IntPtr.Zero;
        private          CancellationTokenSource _cts     = new();
        private          GameState?         _lastState    = null;
        private          bool               _disposed     = false;

        /// <summary>Fired on the thread-pool whenever map, gamemode, or operator changes.</summary>
        public event Action<GameState>? StateChanged;

        // ── Constructor ───────────────────────────────────────────────────────

        public GameMemoryService(Process gameProcess)
        {
            _gameProcess = gameProcess;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Opens the process handle and starts the poll loop.</summary>
        public void Start()
        {
            _handle = OpenProcess(
                MemoryOffsets.PROCESS_VM_READ | MemoryOffsets.PROCESS_QUERY_INFORMATION,
                false,
                _gameProcess.Id);

            if (_handle == IntPtr.Zero)
            {
                Log("OpenProcess failed — check that RainbowSix.exe is running and not elevated.");
                return;
            }

            _moduleBase = GetModuleBase();
            if (_moduleBase == IntPtr.Zero)
            {
                Log("Could not find RainbowSix.exe module base — memory reads will be skipped.");
                return;
            }

            Log($"Module base: 0x{_moduleBase.ToInt64():X}");

            _ = PollLoopAsync(_cts.Token);
        }

        /// <summary>Stops the poll loop and closes the handle.</summary>
        public void Stop()
        {
            _cts.Cancel();
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        // ── Poll loop ─────────────────────────────────────────────────────────

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var state = ReadGameState();
                    if (state != null && HasChanged(state))
                    {
                        _lastState = state;
                        StateChanged?.Invoke(state);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Poll error: {ex.Message}");
                }

                await Task.Delay(MemoryOffsets.PollIntervalMs, ct).ConfigureAwait(false);
            }
        }

        // ── Memory Layer: raw reads ────────────────────────────────────────────

        /// <summary>
        /// Reads game state by following the pointer chains defined in MemoryOffsets.
        /// Returns null if any pointer dereference fails (e.g. game not yet fully loaded).
        /// </summary>
        private GameState? ReadGameState()
        {
            if (_handle == IntPtr.Zero || _moduleBase == IntPtr.Zero) return null;

            // ── Map UID + Gamemode ─────────────────────────────────────────────
            IntPtr mapObject = FollowChain(_moduleBase, MemoryOffsets.MapBase, MemoryOffsets.MapPtrChain);
            if (mapObject == IntPtr.Zero) return null;

            ulong mapUid     = ReadUInt64(mapObject + MemoryOffsets.MapUidOffset);
            int   gamemodeId = ReadInt32(mapObject  + MemoryOffsets.GamemodeOffset);

            // ── Operator ID ────────────────────────────────────────────────────
            IntPtr opObject  = FollowChain(_moduleBase, MemoryOffsets.OperatorBase, MemoryOffsets.OperatorPtrChain);
            int   operatorId = opObject != IntPtr.Zero
                ? ReadInt32(opObject + MemoryOffsets.OperatorIdOffset)
                : -1;

            // ── Debug output ───────────────────────────────────────────────────
            Log($"Map UID raw = 0x{mapUid:X16}  |  Gamemode ID = {gamemodeId}  |  Operator ID = {operatorId}");

            // ── Mapper Layer ───────────────────────────────────────────────────
            return new GameState(
                MapUid:       mapUid,
                GamemodeId:   gamemodeId,
                OperatorId:   operatorId,
                MapName:      GameStateMapper.MapName(mapUid),
                GamemodeName: GameStateMapper.GamemodeName(gamemodeId),
                OperatorName: operatorId >= 0 ? GameStateMapper.OperatorName(operatorId) : null
            );
        }

        // ── Parser Layer: pointer chains ──────────────────────────────────────

        /// <summary>
        /// Follows a pointer chain starting at moduleBase + baseOffset,
        /// dereferencing at each hop in ptrChain.
        /// Returns the final object address, or IntPtr.Zero on failure.
        ///
        /// Example with baseOffset=0x04B3C120, ptrChain=[0x60, 0x10]:
        ///   addr = *(moduleBase + 0x04B3C120)
        ///   addr = *(addr + 0x60)
        ///   addr = *(addr + 0x10)
        ///   → returns addr (which is your MapObject)
        /// </summary>
        private IntPtr FollowChain(IntPtr moduleBase, long baseOffset, int[] ptrChain)
        {
            // First dereference: base pointer
            IntPtr addr = DerefPointer(moduleBase + (int)baseOffset);
            if (addr == IntPtr.Zero) return IntPtr.Zero;

            // Follow each intermediate hop
            foreach (int offset in ptrChain)
            {
                addr = DerefPointer(addr + offset);
                if (addr == IntPtr.Zero) return IntPtr.Zero;
            }

            return addr;
        }

        // ── Memory primitives ─────────────────────────────────────────────────

        private IntPtr DerefPointer(IntPtr address)
        {
            byte[] buf = new byte[8];
            if (!ReadProcessMemory(_handle, address, buf, 8, out int read) || read != 8)
                return IntPtr.Zero;
            long value = BitConverter.ToInt64(buf, 0);
            return value == 0 ? IntPtr.Zero : (IntPtr)value;
        }

        private ulong ReadUInt64(IntPtr address)
        {
            byte[] buf = new byte[8];
            if (!ReadProcessMemory(_handle, address, buf, 8, out int read) || read != 8)
                return 0;
            return BitConverter.ToUInt64(buf, 0);
        }

        private int ReadInt32(IntPtr address)
        {
            byte[] buf = new byte[4];
            if (!ReadProcessMemory(_handle, address, buf, 4, out int read) || read != 4)
                return -1;
            return BitConverter.ToInt32(buf, 0);
        }

        // ── Module base ───────────────────────────────────────────────────────

        private IntPtr GetModuleBase()
        {
            try
            {
                _gameProcess.Refresh();
                foreach (ProcessModule mod in _gameProcess.Modules)
                {
                    if (mod.ModuleName.Equals("RainbowSix.exe", StringComparison.OrdinalIgnoreCase))
                        return mod.BaseAddress;
                }
            }
            catch (Exception ex)
            {
                Log($"GetModuleBase error: {ex.Message}");
            }
            return IntPtr.Zero;
        }

        // ── Change detection ──────────────────────────────────────────────────

        private bool HasChanged(GameState current)
        {
            if (_lastState == null) return true;
            return current.MapUid     != _lastState.MapUid
                || current.GamemodeId != _lastState.GamemodeId
                || current.OperatorId != _lastState.OperatorId;
        }

        // ── Logging ───────────────────────────────────────────────────────────

        private static void Log(string msg)
        {
            if (DEBUG_LOG)
                System.Diagnostics.Debug.WriteLine($"[GameMemory] {msg}");
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts.Dispose();
        }
    }
}

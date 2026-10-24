using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private Process? _gameProcess;
        private SeasonPatch? _sessionPatch;
        private DateTime _sessionStartTime;
        private DispatcherTimer? _sessionTimer;
        private DispatcherTimer? _runningPoll;
        private GameMemoryService? _memoryService;
        private GlobalHotkeyService? _hotkeyService;
        private OverlayWindow? _overlayWindow;
        private bool _launching;
        private List<GameProcessService.RunningGame> _runningGames = new();
        private string _runningSignature = "";

        private bool IsPatchRunning(SeasonPatch p) =>
            (_sessionPatch?.Id == p.Id && _gameProcess is { HasExited: false }) ||
            (p.IsInstalled && GameProcessService.IsRunningFrom(p.InstallDir, _runningGames));

        private bool AnyGameRunning => (_gameProcess is { HasExited: false }) || _runningGames.Count > 0;

        private async Task LaunchPatchAsync(SeasonPatch patch)
        {
            if (_launching) return;
            if (AnyGameRunning)
            {
                ShowToast("A game is already running. Stop it first.", Icons.Warning);
                return;
            }
            if (_downloads.IsBusyWith(patch))
            {
                ShowToast("This season is downloading or verifying. Wait for it to finish.", Icons.Warning);
                return;
            }

            _launching = true;
            Process? process;
            try
            {
                S.Renderer.TryGetValue(patch.Id, out string? renderer);
                S.LaunchArgs.TryGetValue(patch.Id, out string? args);
                process = await _libraryService.LaunchSeasonAsync(patch, renderer, args);
            }
            finally { _launching = false; }

            if (process == null)
            {
                ThemedDialog.Alert(this, "Launch failed", "No game executable was found in this season's folder. Try Verify files.");
                return;
            }

            _gameProcess = process;
            StartSession(patch);
            StartLiberator(patch, S.Renderer.GetValueOrDefault(patch.Id));

            string version = $"{patch.BaseName} {patch.Code} Patch {patch.PatchKey}";
            string inGame = LoaderConfig.Read(patch) ?? EffectiveInGameName();
            if (S.DiscordPresence) _ = _discord.SetInGameAsync(patch.BaseName, patch.Code, patch.PatchKey, inGame);
            _ = SendHeartbeatAsync();

            if (S.GameStateReader && MemoryOffsets.IsConfigured)
            {
                _memoryService?.Dispose();
                _memoryService = new GameMemoryService(process);
                _memoryService.StateChanged += async state =>
                {
                    if (S.DiscordPresence) await _discord.SetGameStateAsync(state.MapName, state.GamemodeName, state.OperatorName);
                    _overlayWindow?.UpdateGameState(state.MapName, state.GamemodeName, state.OperatorName);
                };
                _memoryService.Start();
            }

            if (S.InGameOverlay)
            {
                _overlayWindow ??= new OverlayWindow(_community, _playtime);
                _overlayWindow.SetSessionInfo(version, _sessionStartTime);
                _hotkeyService?.Dispose();
                _hotkeyService = new GlobalHotkeyService();
                _hotkeyService.OverlayToggleRequested += () => Dispatcher.Invoke(() => _overlayWindow?.Toggle());
                _hotkeyService.Start();
            }

            RefreshRunningUi();
            _ = Task.Run(async () =>
            {
                try { process.WaitForExit(); } catch { }
                await Dispatcher.InvokeAsync(EndSession);
                if (S.DiscordPresence) await _discord.SetGameEndedAsync();
            });
        }

        private void StartSession(SeasonPatch patch)
        {
            _sessionPatch = patch;
            _sessionStartTime = DateTime.UtcNow;
            _playtime.StartSession(patch.Id);
            _sessionTimer?.Stop();
            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += (_, _) => UpdateNowPlaying();
            _sessionTimer.Start();
            UpdateNowPlaying();
        }

        private void EndSession()
        {
            if (_sessionPatch == null) return;
            _playtime.EndSession();
            _sessionTimer?.Stop();
            _sessionTimer = null;
            _sessionPatch = null;
            _gameProcess = null;
            _memoryService?.Dispose();
            _memoryService = null;
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            _overlayWindow?.Hide();
            StopLiberator();
            NowPlayingCard.Visibility = Visibility.Collapsed;
            _runningGames = GameProcessService.Running();
            RefreshRunningUi();
            UpdateStatusBar();
            _ = SendHeartbeatAsync();
            _ = PublishProfileAsync();
        }

        private void UpdateNowPlaying()
        {
            if (_sessionPatch == null) { NowPlayingCard.Visibility = Visibility.Collapsed; return; }
            NowPlayingCard.Visibility = Visibility.Visible;
            NowPlayingTitle.Text = _liberator?.State.Unlocked == true ? $"{_sessionPatch.ShortLabel}  ·  Unlocked" : _sessionPatch.ShortLabel;
            var t = DateTime.UtcNow - _sessionStartTime;
            NowPlayingTimer.Text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        private void StopGame_Click(object sender, RoutedEventArgs e) => StopGame();

        /// <summary>Kills the tracked process tree and any known Siege process.</summary>
        private void StopGame()
        {
            GameProcessService.StopAll(_gameProcess);
            ShowToast("Game stopped", Icons.Stop);
        }

        private void StartRunningGamePoll()
        {
            _runningPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _runningPoll.Tick += async (_, _) =>
            {
                var running = await Task.Run(GameProcessService.Running);
                _runningGames = running;
                string sig = string.Join(",", running.Select(r => r.Pid));
                if (sig == _runningSignature) return;
                _runningSignature = sig;
                RefreshRunningUi();
            };
            _runningPoll.Start();
        }

        private void RefreshRunningUi()
        {
            if (_currentView == HomeView) BuildHome();
            else if (_currentView == BrowseView) BuildBrowse();
            else if (_currentView == DetailView && _detailPatch != null) UpdateDetailLive();
            RefreshSidebarStatus();
        }
    }
}

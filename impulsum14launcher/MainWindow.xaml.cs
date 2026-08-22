using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ImpulsumLauncher14.Models;
using ImpulsumLauncher14.Services;

namespace ImpulsumLauncher14;

public partial class MainWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly GameService _gameService;
    private readonly ServerService _serverService;
    private readonly DispatcherTimer _statusTimer;
    private Process? _gameProcess;
    private bool _gameLaunching;
    private bool _isClosing;
    private readonly CancellationTokenSource _launchCancellation = new();
    private bool _versionSupported = true;
    private string _currentGamePath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { UpdateBannerClip(); UpdateRootClip(); };
        BannerBorder.SizeChanged += (_, _) => UpdateBannerClip();
        RootBorder.SizeChanged += (_, _) => UpdateRootClip();

        _config = LauncherConfig.Load();
        _gameService = new GameService();
        _serverService = new ServerService();

        _serverService.StatusChanged += OnServerStatusChanged;

        LoadConfig();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => CheckProcesses();
        _statusTimer.Start();

        CheckProcesses();
    }

    private void LoadConfig()
    {
        if (!string.IsNullOrEmpty(_config.GamePath) && _gameService.ValidatePath(_config.GamePath))
        {
            _currentGamePath = _config.GamePath;
            GamePathBox.Text = _currentGamePath;
            UpdatePathStatus();
            ShowPlayScreen();
        }
    }

    private void ShowPlayScreen()
    {
        if (string.IsNullOrEmpty(_currentGamePath) || !_gameService.ValidatePath(_currentGamePath))
            return;

        var version = _gameService.GetFileVersion(_currentGamePath);
        if (version != null)
        {
            VersionText.Text = $"Version {version}";
            _versionSupported = version.Major == 1 && version.Minor == 7 &&
                                version.Build == 0 && version.Revision == 0;
        }
        else
        {
            VersionText.Text = "Version unknown";
            _versionSupported = true;
        }

        if (_versionSupported)
        {
            PlayBtn.IsEnabled = true;
            PlayBtn.Opacity = 1.0;
            ServerStatusDot.Fill = (Brush)FindResource("SuccessBrush");
            ServerStatusText.Text = "Ready";
        }
        else
        {
            PlayBtn.IsEnabled = false;
            PlayBtn.Opacity = 0.35;
            ServerStatusDot.Fill = (Brush)FindResource("ErrorBrush");
            ServerStatusText.Text = "Unsupported version";
        }

        SetupScreen.Visibility = Visibility.Collapsed;
        PlayScreen.Visibility = Visibility.Visible;
    }

    private void UpdatePathStatus()
    {
        if (ContinueBtn == null) return;
        ContinueBtn.IsEnabled = _gameService.ValidatePath(_currentGamePath);
    }

    private void CheckProcesses()
    {
        if (!_versionSupported)
            return;

        if (!(_gameProcess is { HasExited: false }) && _gameLaunching)
        {
            _gameLaunching = false;
            PlayBtn.IsEnabled = true;
            PlayBtn.Opacity = 1.0;
        }

        if (_gameLaunching)
        {
            ServerStatusDot.Fill = (Brush)FindResource("AccentBrush");
            ServerStatusText.Text = "Starting server…";
        }
        else if (_serverService.IsRunning)
        {
            ServerStatusDot.Fill = (Brush)FindResource("SuccessBrush");
            ServerStatusText.Text = "Server running";
        }
        else
        {
            ServerStatusDot.Fill = (Brush)FindResource("SuccessBrush");
            ServerStatusText.Text = "Ready";
        }

        UpdatePlayButtonState();
    }

    private void UpdatePlayButtonState()
    {
        if (_serverService.IsRunning)
        {
            PlayBtn.Content = "Close";
            PlayBtn.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            return;
        }

        PlayBtn.Content = "Play";
        PlayBtn.ClearValue(Button.BackgroundProperty);
    }

    private void StopGameProcesses()
    {
        var processes = new List<Process>();

        if (_gameProcess is { HasExited: false })
            processes.Add(_gameProcess);

        AddProcessesByName(processes, "fifa14");
        AddProcessesByName(processes, "fifaconfig");

        foreach (var process in processes.DistinctBy(process => process.Id))
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }

        _gameProcess = null;
    }

    private static void AddProcessesByName(List<Process> processes, string processName)
    {
        try
        {
            processes.AddRange(Process.GetProcessesByName(processName));
        }
        catch { }
    }

    private void OnServerStatusChanged(bool running)
    {
        Dispatcher.Invoke(CheckProcesses);
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsOverlay();
    }


    private readonly ProfileService _profileService = new();
    private readonly DisplayConfigService _displayConfigService = new();

    private static readonly (int Width, int Height)[] CommonResolutions =
    {
        (1280, 720),
        (1366, 768),
        (1600, 900),
        (1920, 1080),
        (2560, 1440),
        (3840, 2160),
    };

    private bool _suppressQualityEvents;
    private bool _formattingCoins;

    private void ShowSettingsOverlay()
    {
        if (_profileService.TryGet(out var personaName, out var coins))
        {
            SettingsUsernameBox.Text = personaName;
            SettingsCoinsBox.Text = coins.ToString("N0", CultureInfo.InvariantCulture);
        }

        LoadDisplaySettings();

        ProfileTabBtn_Click(this, new RoutedEventArgs());

        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsDim.Opacity = 0;
        var fade = new DoubleAnimation(0, 0.4, TimeSpan.FromMilliseconds(220));
        SettingsDim.BeginAnimation(OpacityProperty, fade);

        var pop = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        SettingsCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        SettingsCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void HideSettingsOverlay()
    {
        var fade = new DoubleAnimation(SettingsDim.Opacity, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => SettingsOverlay.Visibility = Visibility.Collapsed;
        SettingsDim.BeginAnimation(OpacityProperty, fade);
    }

    private void SettingsCoinsBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void SettingsCoinsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formattingCoins) return;

        var text = SettingsCoinsBox.Text;
        var caretIndex = SettingsCoinsBox.CaretIndex;
        var digitsBeforeCaret = text[..caretIndex].Count(character => character >= '0' && character <= '9');
        var digits = new string(text.Where(character => character >= '0' && character <= '9').ToArray());

        if (digits.Length == 0)
            return;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var coins))
            coins = ProfileService.MaxCoins;
        else
            coins = Math.Min(coins, ProfileService.MaxCoins);

        var formatted = coins.ToString("N0", CultureInfo.InvariantCulture);
        if (formatted == text)
            return;

        _formattingCoins = true;
        try
        {
            SettingsCoinsBox.Text = formatted;
            SettingsCoinsBox.CaretIndex = formatted
                .Select((character, index) => (character, index))
                .Where(item => item.character >= '0' && item.character <= '9')
                .Skip(Math.Min(digitsBeforeCaret, digits.Length))
                .Select(item => item.index + 1)
                .FirstOrDefault(formatted.Length);
        }
        finally
        {
            _formattingCoins = false;
        }
    }

    private void SettingsCoinsBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SettingsCoinsBox.CaretIndex = SettingsCoinsBox.Text.Length;
    }

    private void SettingsSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(SettingsCoinsBox.Text.Trim(), NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture, out var coins) ||
            coins < 0 || coins > ProfileService.MaxCoins)
        {
            MessageBox.Show(this, "Please enter a valid coin balance.", "Invalid Value",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profileOk = _profileService.TryUpdate(SettingsUsernameBox.Text, coins);
        var displayOk = SaveDisplaySettings();

        if (profileOk && displayOk)
        {
            MessageBox.Show(this, "Settings updated.\nRestart the game to see the changes.",
                "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            HideSettingsOverlay();
        }
        else
        {
            var what = !profileOk ? "user.json" : "fifasetup.ini";
            MessageBox.Show(this, $"Could not update {what}.\n" +
                                  "Make sure the Server folder is next to the launcher, and that " +
                                  "the launcher has permission to write to your Documents folder.",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SettingsCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        HideSettingsOverlay();
    }


    private void ProfileTabBtn_Click(object sender, RoutedEventArgs e)
    {
        ProfileTabPanel.Visibility = Visibility.Visible;
        DisplayTabPanel.Visibility = Visibility.Collapsed;
        ProfileTabBtn.Opacity = 1.0;
        DisplayTabBtn.Opacity = 0.55;
    }

    private void DisplayTabBtn_Click(object sender, RoutedEventArgs e)
    {
        ProfileTabPanel.Visibility = Visibility.Collapsed;
        DisplayTabPanel.Visibility = Visibility.Visible;
        ProfileTabBtn.Opacity = 0.55;
        DisplayTabBtn.Opacity = 1.0;
    }


    private void LoadDisplaySettings()
    {
        ResolutionCombo.Items.Clear();
        foreach (var (w, h) in CommonResolutions)
            ResolutionCombo.Items.Add($"{w} x {h}");

        _displayConfigService.TryGet(out var settings);

        var current = $"{settings.ResolutionWidth} x {settings.ResolutionHeight}";
        if (!ResolutionCombo.Items.Contains(current))
            ResolutionCombo.Items.Add(current);
        ResolutionCombo.SelectedItem = current;

        FullscreenRadio.IsChecked = settings.FullScreen;
        WindowedRadio.IsChecked = !settings.FullScreen;

        _suppressQualityEvents = true;
        QualitySuperLowRadio.IsChecked = settings.RenderingQuality == 0;
        QualityLowRadio.IsChecked = settings.RenderingQuality == 1;
        QualityMediumRadio.IsChecked = settings.RenderingQuality == 2;
        QualityHighRadio.IsChecked = settings.RenderingQuality == 3;
        _suppressQualityEvents = false;
        UpdateQualityDescription(settings.RenderingQuality);

        VsyncCombo.SelectedIndex = Math.Clamp(settings.WaitForVsync, 0, 2);
        MsaaCombo.SelectedIndex = settings.MsaaLevel switch
        {
            2 => 1,
            4 => 2,
            _ => 0,
        };
        ControllerDefaultCombo.SelectedIndex = Math.Clamp(settings.ControllerDefault, 0, 1);

        ScreenSleepCheckBox.IsChecked = settings.ScreenSleep;
        DisableAeroCheckBox.IsChecked = settings.DisableWindowsAero;
        VoiceChatCheckBox.IsChecked = settings.VoiceChat;
    }

    private void QualityRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressQualityEvents) return;
        UpdateQualityDescription(GetSelectedQuality());
    }

    private int GetSelectedQuality()
    {
        if (QualitySuperLowRadio.IsChecked == true) return 0;
        if (QualityLowRadio.IsChecked == true) return 1;
        if (QualityMediumRadio.IsChecked == true) return 2;
        return 3;
    }

    private void UpdateQualityDescription(int quality)
    {
        QualityDescriptionText.Text = quality switch
        {
            0 => "Extremely low rendering quality — use this to maximize frame rate.",
            1 => "In-game rendering quality will be low, however frame rate will be faster.",
            2 => "Balances rendering quality and frame rate.",
            3 => "In-game rendering quality will be high, however frame rate will be slower.",
            _ => string.Empty,
        };
    }

    private bool SaveDisplaySettings()
    {
        var parts = (ResolutionCombo.SelectedItem as string)?.Split('x');
        var width = 1920;
        var height = 1080;
        if (parts is { Length: 2 } &&
            int.TryParse(parts[0].Trim(), out var w) &&
            int.TryParse(parts[1].Trim(), out var h))
        {
            width = w;
            height = h;
        }

        var settings = new DisplaySettings
        {
            ResolutionWidth = width,
            ResolutionHeight = height,
            FullScreen = FullscreenRadio.IsChecked == true,
            RenderingQuality = GetSelectedQuality(),
            WaitForVsync = VsyncCombo.SelectedIndex,
            MsaaLevel = MsaaCombo.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 0,
            },
            ControllerDefault = ControllerDefaultCombo.SelectedIndex,
            ScreenSleep = ScreenSleepCheckBox.IsChecked == true,
            DisableWindowsAero = DisableAeroCheckBox.IsChecked == true,
            VoiceChat = VoiceChatCheckBox.IsChecked == true,
        };

        return _displayConfigService.TryUpdate(settings);
    }

    private void SaveConfig()
    {
        _config.GamePath = _currentGamePath;
        _config.Save();
    }

    private void UpdateBannerClip()
    {
        if (BannerBorder == null || BannerBorder.ActualWidth <= 0 || BannerBorder.ActualHeight <= 0)
            return;
        BannerBorder.Clip = new RectangleGeometry(
            new System.Windows.Rect(0, 0, BannerBorder.ActualWidth, BannerBorder.ActualHeight),
            12, 12);
    }

    private void UpdateRootClip()
    {
        if (RootBorder == null || RootBorder.ActualWidth <= 0 || RootBorder.ActualHeight <= 0)
            return;
        RootBorder.Clip = new RectangleGeometry(
            new System.Windows.Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight),
            10, 10);
    }


    private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentGamePath = GamePathBox.Text.Trim();
        if (PlaceholderText != null)
            PlaceholderText.Visibility = string.IsNullOrEmpty(GamePathBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        UpdatePathStatus();
    }

    private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the FIFA 14 Game folder (containing fifa14.exe)",
            SelectedPath = _currentGamePath,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _currentGamePath = dialog.SelectedPath;
            GamePathBox.Text = _currentGamePath;
            UpdatePathStatus();
        }
    }

    private void DetectGameBtn_Click(object sender, RoutedEventArgs e)
    {
        var path = _gameService.FindDefaultPath();
        if (!string.IsNullOrEmpty(path))
        {
            _currentGamePath = path;
            GamePathBox.Text = _currentGamePath;
            UpdatePathStatus();
        }
        else
        {
            MessageBox.Show(this,
                "Could not find FIFA 14 in common locations or registry.\n" +
                "Please select the folder manually using Browse.",
                "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ChangePathBtn_Click(object sender, RoutedEventArgs e)
    {
        GamePathBox.Text = _currentGamePath;
        UpdatePathStatus();
        PlayScreen.Visibility = Visibility.Collapsed;
        SetupScreen.Visibility = Visibility.Visible;
    }

    private void ContinueBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_gameService.ValidatePath(_currentGamePath)) return;

        _config.GamePath = _currentGamePath;
        SaveConfig();
        ShowPlayScreen();
    }


    private async void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing) return;

        if (_serverService.IsRunning)
        {
            _gameLaunching = false;
            StopGameProcesses();
            try
            {
                _serverService.Stop();
            }
            catch { }
            return;
        }

        if (_gameLaunching) return;

        if (string.IsNullOrEmpty(_currentGamePath) || !_gameService.ValidatePath(_currentGamePath))
        {
            SetupScreen.Visibility = Visibility.Visible;
            PlayScreen.Visibility = Visibility.Collapsed;
            MessageBox.Show(this,
                "Game path is invalid. Please reconfigure.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        PlayBtn.IsEnabled = false;
        PlayBtn.Opacity = 0.6;
        _gameLaunching = true;

        _displayConfigService.EnsureDefaultsExist();

        if (!_serverService.IsRunning)
        {
            ServerStatusDot.Fill = (Brush)FindResource("AccentBrush");
            var updateOk = await _serverService.UpdateAndBuildServerAsync(_config, status => Dispatcher.Invoke(() => ServerStatusText.Text = status));
            if (_isClosing) return;
            if (!updateOk)
            {
                MessageBox.Show(this,
                    "Failed to download or build the server, and no existing version was found. Check your connection.",
                    "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                _gameLaunching = false;
                PlayBtn.IsEnabled = true;
                PlayBtn.Opacity = 1.0;
                return;
            }
            
            ServerStatusText.Text = "Starting server…";

            var serverResult = await _serverService.StartAsync(_config.ServerPath, _launchCancellation.Token);
            if (_isClosing) return;
            if (!serverResult.Success)
            {
                _gameLaunching = false;
                PlayBtn.IsEnabled = true;
                PlayBtn.Opacity = 1.0;
                MessageBox.Show(this, serverResult.ErrorMessage, "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var patchError = _gameService.ApplyDllPatches(_currentGamePath);
        if (!string.IsNullOrEmpty(patchError))
        {
            _gameLaunching = false;
            PlayBtn.IsEnabled = true;
            PlayBtn.Opacity = 1.0;
            MessageBox.Show(this, patchError, "Patch Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var result = await _gameService.LaunchGameAsync(_currentGamePath);
        if (_isClosing)
        {
            if (result.Success)
            {
                try
                {
                    using var process = Process.GetProcessById(result.ProcessId);
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
            }
            return;
        }
        if (result.Success)
        {
            _gameProcess = Process.GetProcessById(result.ProcessId);
            _gameProcess.EnableRaisingEvents = true;
            _gameProcess.Exited += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _gameLaunching = false;
                    PlayBtn.IsEnabled = true;
                    PlayBtn.Opacity = 1.0;
                    CheckProcesses();
                });
            };

            _gameLaunching = false;
            PlayBtn.IsEnabled = true;
            PlayBtn.Opacity = 1.0;
            CheckProcesses();
        }
        else
        {
            _gameLaunching = false;
            PlayBtn.IsEnabled = true;
            PlayBtn.Opacity = 1.0;
            MessageBox.Show(this, result.ErrorMessage, "Launch Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _isClosing = true;
        _launchCancellation.Cancel();
        StopGameProcesses();
        _serverService.Stop();
        SaveConfig();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _launchCancellation.Cancel();
        StopGameProcesses();
        _serverService.Stop();
        _launchCancellation.Dispose();
        SaveConfig();
        base.OnClosed(e);
    }
}
using System.IO;
using System.Reflection;
using NAudio.Wave;
using WpfApp.Services.Utils;

namespace WpfApp.Services.Core;

public class AudioService : IAudioService, IDisposable
{
    private readonly SerilogManager _logger = SerilogManager.Instance;
    private readonly PathService _pathService = PathService.Instance;
    private readonly string _startSoundPath;
    private readonly string _stopSoundPath;
    private WaveOutEvent _outputDevice;
    private MediaFoundationReader _mediaReader;
    private readonly object _lockObject = new();
    private CancellationTokenSource _currentCts;
    private bool _isPlayingStopSound;
    private bool _isDisposed;
    private readonly object _disposeLock = new();
    private bool _audioDeviceAvailable = true;
    private double _volume = 0.8;

    public bool IsDisposed => _isDisposed;
    public bool AudioDeviceAvailable => _audioDeviceAvailable;

    public double Volume
    {
        get => _volume;
        set
        {
            // 将值限制在0.0-1.0范围内，并保留两位小数
            var newValue = Math.Round(Math.Max(0.0, Math.Min(1.0, value)), 2);

            // 仅当音量变化超过阈值时才更新
            if (Math.Abs(_volume - newValue) >= 0.001)
            {
                _volume = newValue;

                // 当有活动音频设备时，立即应用音量设置
                lock (_lockObject)
                {
                    if (_outputDevice != null && _audioDeviceAvailable)
                        try
                        {
                            _outputDevice.Volume = (float)_volume;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("设置音频音量失败", ex);
                        }
                }
            }
        }
    }

    public AudioService()
    {
        try
        {
            _startSoundPath = _pathService.GetSoundFilePath("start.mp3");
            _stopSoundPath = _pathService.GetSoundFilePath("stop.mp3");

            EnsureAudioFileExists("start.mp3", _startSoundPath);
            EnsureAudioFileExists("stop.mp3", _stopSoundPath);

            if (!File.Exists(_startSoundPath) || !File.Exists(_stopSoundPath))
            {
                _logger.Error("音频文件不存在");
                throw new FileNotFoundException("音频文件不存在");
            }

            try
            {
                using (var testReader = new MediaFoundationReader(_startSoundPath))
                using (var testDevice = new WaveOutEvent())
                {
                    testDevice.Init(testReader);
                }
            }
            catch (Exception ex)
            {
                _audioDeviceAvailable = false;
                _logger.Error("音频设备不可用", ex);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("音频服务初始化失败", ex);
            throw;
        }
    }

    private void EnsureAudioFileExists(string fileName, string targetPath)
    {
        try
        {
            if (!File.Exists(targetPath))
            {
                var resourceName = $"WpfApp.Resource.sound.{fileName}";

                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream is null)
                    {
                        _logger.Error($"找不到音频资源: {resourceName}");
                        throw new FileNotFoundException($"找不到音频资源: {resourceName}");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                    using (var fileStream = File.Create(targetPath))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"提取音频文件失败: {fileName}", ex);
            throw;
        }
    }

    public void PlayStartSound()
    {
        if (!_audioDeviceAvailable) return;

        lock (_lockObject)
        {
            if (_isPlayingStopSound) DisposeCurrentSound();
        }

        PlaySound(_startSoundPath, true);
    }

    public void PlayStopSound()
    {
        if (!_audioDeviceAvailable) return;

        PlaySound(_stopSoundPath, false);
    }

    private void PlaySound(string path, bool isStartSound)
    {
        if (!File.Exists(path))
        {
            _logger.Error($"音频文件不存在: {path}");
            return;
        }

        try
        {
            lock (_lockObject)
            {
                // 取消之前的播放任务（如果有）
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                // 如果当前有音效在播放，停止它
                if (_outputDevice != null) DisposeCurrentSound();

                _isPlayingStopSound = !isStartSound;
            }

            var cts = _currentCts;

            // 创建新的播放实例
            var mediaReader = new MediaFoundationReader(path);
            var outputDevice = new WaveOutEvent();

            // 设置音量
            outputDevice.Volume = (float)_volume;

            lock (_lockObject)
            {
                if (cts.IsCancellationRequested)
                {
                    mediaReader.Dispose();
                    outputDevice.Dispose();
                    return;
                }

                _mediaReader = mediaReader;
                _outputDevice = outputDevice;
            }

            var tcs = new TaskCompletionSource<bool>();
            outputDevice.PlaybackStopped += (s, e) =>
            {
                tcs.TrySetResult(true);
                lock (_lockObject)
                {
                    if (_outputDevice == outputDevice)
                    {
                        _isPlayingStopSound = false;
                        DisposeCurrentSound();
                    }
                }
            };

            outputDevice.Init(mediaReader);
            outputDevice.Play();

            // 使用线程等待播放完成，不阻塞调用线程
            Thread waitThread = new Thread(() =>
            {
                try
                {
                    // 等待播放完成或取消
                    while (!cts.IsCancellationRequested && outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("等待音频播放完成时出错", ex);
                }
            })
            {
                IsBackground = true
            };
            waitThread.Start();
        }
        catch (Exception ex)
        {
            _logger.Error($"播放声音失败: {path}", ex);
            lock (_lockObject)
            {
                _isPlayingStopSound = false;
                DisposeCurrentSound();
            }
        }
    }

    private void DisposeCurrentSound()
    {
        try
        {
            if (_outputDevice != null)
            {
                _outputDevice.Stop();
                _outputDevice.Dispose();
                _outputDevice = null;
            }

            if (_mediaReader != null)
            {
                _mediaReader.Dispose();
                _mediaReader = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("释放当前音频资源时发生异常", ex);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                // 取消当前播放任务
                if (_currentCts != null)
                {
                    _currentCts.Cancel();
                    _currentCts.Dispose();
                    _currentCts = null;
                }

                // 停止并释放当前音频
                lock (_lockObject)
                {
                    _isPlayingStopSound = false;
                    DisposeCurrentSound();
                }

                _logger.Debug("音频服务资源已释放");
            }
            catch (Exception ex)
            {
                _logger.Error("释放音频服务资源时发生异常", ex);
            }
        }

        GC.SuppressFinalize(this);
    }
}
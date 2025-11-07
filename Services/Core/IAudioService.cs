namespace WpfApp.Services.Core;

/// <summary>
/// 音频服务接口
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>
    /// 播放启动音效
    /// </summary>
    void PlayStartSound();

    /// <summary>
    /// 播放停止音效
    /// </summary>
    void PlayStopSound();

    /// <summary>
    /// 音量（0.0-1.0）
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// 是否已释放
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// 音频设备是否可用
    /// </summary>
    bool AudioDeviceAvailable { get; }
}

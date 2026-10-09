using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SlimClip.UI.Animation;

/// <summary>
/// Reusable 60fps animator for tab/filter capsules.
/// Produces a distinct two-phase staggered edge transition:
/// 1. Leading edge shoots forward monotonically to destination edge (stretching).
/// 2. Trailing edge follows monotonically to destination edge (retracting).
/// Zero overshoot, zero oscillation, and zero inward shrinking.
/// </summary>
public sealed class ElasticPillAnimator
{
    private readonly Border _pill;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();

    private double _startX;
    private double _startWidth;
    private double _targetX;
    private double _targetWidth;
    private double _currentX;
    private double _currentWidth;
    private Action? _onCompleted;

    private const double DurationMs = 280.0;
    private const double LeadPhaseEnd = 0.70;
    private const double TrailPhaseStart = 0.20;

    public bool IsRunning { get; private set; }

    public ElasticPillAnimator(Border pill)
    {
        _pill = pill ?? throw new ArgumentNullException(nameof(pill));
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
    }

    public void Animate(double fromX, double fromWidth, double toX, double toWidth, Action? onCompleted = null)
    {
        if (IsRunning)
        {
            _onCompleted?.Invoke();
            _startX = _currentX;
            _startWidth = _currentWidth;
        }
        else
        {
            _startX = fromX;
            _startWidth = fromWidth;
        }

        _targetX = toX;
        _targetWidth = toWidth;
        _currentX = _startX;
        _currentWidth = _startWidth;
        _onCompleted = onCompleted;

        _pill.Margin = new Thickness(_startX, 0, 0, 0);
        _pill.Width = _startWidth;
        _pill.IsVisible = true;

        _stopwatch.Restart();
        _timer.Start();
        IsRunning = true;
    }

    public void Cancel()
    {
        if (IsRunning)
        {
            _timer.Stop();
            _stopwatch.Stop();
            IsRunning = false;
            var cb = _onCompleted;
            _onCompleted = null;
            cb?.Invoke();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double elapsed = _stopwatch.Elapsed.TotalMilliseconds;
        double t = Math.Clamp(elapsed / DurationMs, 0.0, 1.0);

        double distance = _targetX - _startX;
        bool movingRight = distance >= 0;

        if (movingRight)
        {
            // Right is leading edge (moves first), Left is trailing edge (moves second)
            double rStart = _startX + _startWidth;
            double rTarget = _targetX + _targetWidth;

            // 1. Leading edge R(t): smoothly advances from rStart to rTarget in [0.0, LeadPhaseEnd]
            // Monotonic: strictly non-decreasing, NEVER moves backwards!
            double r;
            if (t <= LeadPhaseEnd)
            {
                double uLead = t / LeadPhaseEnd;
                double eLead = 1.0 - Math.Pow(1.0 - uLead, 3.0);
                r = rStart + (rTarget - rStart) * eLead;
            }
            else
            {
                r = rTarget;
            }

            // 2. Trailing edge L(t): anchored at _startX until TrailPhaseStart, then advances to _targetX
            // Monotonic: strictly non-decreasing, NEVER overshoots or bounces!
            double l;
            if (t < TrailPhaseStart)
            {
                l = _startX;
            }
            else
            {
                double uTrail = (t - TrailPhaseStart) / (1.0 - TrailPhaseStart);
                double eTrail = 1.0 - Math.Pow(1.0 - uTrail, 3.0);
                l = _startX + (_targetX - _startX) * eTrail;
            }

            _currentX = Math.Min(l, r - 20.0);
            _currentWidth = Math.Max(20.0, r - _currentX);
        }
        else
        {
            // Left is leading edge (moves first), Right is trailing edge (moves second)
            double rStart = _startX + _startWidth;
            double rTarget = _targetX + _targetWidth;

            // 1. Leading edge L(t): smoothly advances left from _startX to _targetX in [0.0, LeadPhaseEnd]
            // Monotonic: strictly non-increasing, NEVER bounces right!
            double l;
            if (t <= LeadPhaseEnd)
            {
                double uLead = t / LeadPhaseEnd;
                double eLead = 1.0 - Math.Pow(1.0 - uLead, 3.0);
                l = _startX + (_targetX - _startX) * eLead;
            }
            else
            {
                l = _targetX;
            }

            // 2. Trailing edge R(t): anchored at rStart until TrailPhaseStart, then advances left to rTarget
            // Monotonic: strictly non-increasing, NEVER overshoots left!
            double r;
            if (t < TrailPhaseStart)
            {
                r = rStart;
            }
            else
            {
                double uTrail = (t - TrailPhaseStart) / (1.0 - TrailPhaseStart);
                double eTrail = 1.0 - Math.Pow(1.0 - uTrail, 3.0);
                r = rStart + (rTarget - rStart) * eTrail;
            }

            _currentX = l;
            _currentWidth = Math.Max(20.0, r - l);
        }

        if (t >= 1.0)
        {
            _timer.Stop();
            _stopwatch.Stop();
            IsRunning = false;
            _currentX = _targetX;
            _currentWidth = _targetWidth;
            _pill.Margin = new Thickness(_targetX, 0, 0, 0);
            _pill.Width = _targetWidth;
            var cb = _onCompleted;
            _onCompleted = null;
            cb?.Invoke();
        }
        else
        {
            _pill.Margin = new Thickness(_currentX, 0, 0, 0);
            _pill.Width = _currentWidth;
        }
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Dafeiyv
{
    public class PhysicsHelper
    {
        private readonly Window _window;

        // ---- physics tuning (all values in device-independent units and seconds) ----
        private const double Gravity = 4500.0;  // px/s²  重力加速度
        private const double Restitution = 0.52;    // 弹性系数（越大越弹，1 为完全弹性）
        private const double AirDrag = 0.18;    // 空气中的阻力（越小飞得越远）
        private const double GroundFriction = 8.0;     // 地面滑动摩擦（越大越快停下）
        private const double BounceThreshold = 60.0;    // 低于此落地速度就不再反弹、直接落地
        private const double RestSpeed = 12.0;    // 低于此滑动速度就完全停下
        private const double MaxSpeed = 8000.0;  // 抛飞速度上限

        // GIF 在窗口内的垂直偏移（碰撞用 GIF 的可见范围而不是窗口范围）：
        // 顶部偏移 Margin.Top（110px），否则 GIF 上方会隔着空白就被弹开；
        // 底部偏移 Margin.Bottom（30px），否则 GIF 会悬空 30px 不贴地。
        // 都随宠物缩放联动，在 UpdateBounds 里按 Margin × PetScale 计算。
        private double _topOffset = 110.0;
        private double _bottomOffset = 30.0;

        // ---- state ----
        private double _vx, _vy;   // 当前速度 (px/s)
        private bool _grounded;    // 是否贴地
        private bool _active;      // 物理是否在运行（空中飞行 或 地面滑行）

        // screen bounds
        private double _minX, _minY, _maxX, _floorY;

        // timing
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastTick;

        public bool EnablePhysics { get; set; } = false;
        public bool IsFlying => _active;

        public PhysicsHelper(Window window)
        {
            _window = window;
            _window.Loaded += OnLoaded;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateBounds();
            _lastTick = _clock.Elapsed.TotalSeconds;
        }

        private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateBounds();
        }

        private void UpdateBounds()
        {
            _minX = SystemParameters.VirtualScreenLeft;
            _minY = SystemParameters.VirtualScreenTop;
            _maxX = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - _window.Width;

            // 顶部/底部偏移 = GIF 的 Margin.Top / Margin.Bottom × 宠物缩放，随设置联动
            double topOffset = 110.0, bottomOffset = 30.0;
            if (_window is MainWindow mw && mw.PetImage != null)
            {
                topOffset = mw.PetImage.Margin.Top * mw.PetScale;
                bottomOffset = mw.PetImage.Margin.Bottom * mw.PetScale;
            }
            _topOffset = topOffset;
            _bottomOffset = bottomOffset;

            // 地面：让 GIF 底部（窗口底部 - 底部偏移）贴到工作区底部。
            // 窗口可比工作区底部再低 bottomOffset 一点，那部分正好是透明留白。
            _floorY = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height - _window.Height + bottomOffset;
        }

        public void RefreshBounds() => UpdateBounds();

        private void OnRendering(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastTick;
            _lastTick = now;   // 每帧都更新时间，避免长时间不飞后 dt 巨大

            if (!EnablePhysics || !_active || dt <= 0) return;
            if (dt > 0.05) dt = 0.05;   // 截断大间隔（卡顿 / 从休眠恢复）

            Step(dt);
        }

        private void Step(double dt)
        {
            if (_grounded)
            {
                // 在地面滑行：只有摩擦力，没有重力
                _vx *= Math.Exp(-GroundFriction * dt);
                if (Math.Abs(_vx) < RestSpeed)
                {
                    _vx = 0.0;
                    _active = false;   // 完全停下，物理休眠
                }
            }
            else
            {
                // 空中：重力 + 轻微空气阻力
                _vy += Gravity * dt;
                double drag = Math.Exp(-AirDrag * dt);
                _vx *= drag;
                _vy *= drag;
            }

            double newLeft = _window.Left + _vx * dt;
            double newTop = _window.Top + _vy * dt;

            // 左右墙壁反弹（空中和滑行时都生效）
            if (newLeft <= _minX) { newLeft = _minX; if (_vx < 0) _vx = -_vx * Restitution; }
            else if (newLeft >= _maxX) { newLeft = _maxX; if (_vx > 0) _vx = -_vx * Restitution; }

            if (_grounded)
            {
                newTop = _floorY;   // 滑行时保持贴地
            }
            else
            {
                if (newTop >= _floorY)
                {
                    newTop = _floorY;
                    if (_vy > BounceThreshold)
                    {
                        _vy = -_vy * Restitution;   // 落地反弹
                    }
                    else
                    {
                        _vy = 0.0;
                        _grounded = true;
                        // 若还有水平速度则继续模拟（地面滑行），否则直接停
                        _active = Math.Abs(_vx) >= RestSpeed;
                    }
                }
                else if (newTop + _topOffset <= _minY)
                {
                    // 天花板：让 GIF 顶部（窗口顶部 + 偏移）真正贴到屏幕顶部，
                    // 而不是窗口顶部撞屏幕顶部（那样 GIF 上方还隔着空白）
                    newTop = _minY - _topOffset;
                    if (_vy < 0) _vy = -_vy * Restitution;   // 天花板反弹
                }
            }

            _window.Left = newLeft;
            _window.Top = newTop;
        }

        public void Throw(double vx, double vy)
        {
            if (!EnablePhysics) return;
            _vx = Math.Clamp(vx, -MaxSpeed, MaxSpeed);
            _vy = Math.Clamp(vy, -MaxSpeed, MaxSpeed);
            _grounded = false;
            _active = true;
        }

        public void StartFalling()
        {
            if (!EnablePhysics || _active) return;
            _vx = 0;
            _vy = 60.0;
            _grounded = false;
            _active = true;
        }

        public void Stop()
        {
            _active = false;
            _grounded = false;
            _vx = 0;
            _vy = 0;
        }

        public void Reset()
        {
            Stop();
        }

        public void Unsubscribe()
        {
            CompositionTarget.Rendering -= OnRendering;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        }
    }
}

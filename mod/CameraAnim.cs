using System.Threading;
using UnityEngine;

namespace CS1McpBridge
{
    /// <summary>
    /// Drives a timed, eased camera move (fly_to). Registered by the command on the
    /// main thread, then advanced each frame from Threading.OnUpdate. The calling
    /// socket thread blocks on <see cref="WaitFor"/> until the move completes, so
    /// callers can sequence "fly, then screenshot".
    ///
    /// We write both m_current* and m_target* each frame so the controller's easing
    /// has nothing to interpolate — the camera sits exactly where we put it, giving
    /// us precise control over duration.
    /// </summary>
    public static class CameraAnim
    {
        static bool _active;
        static CameraController _cc;
        static Vector3 _p0, _p1;
        static Vector2 _a0, _a1;
        static float _s0, _s1;
        static float _elapsed, _duration;
        static readonly ManualResetEvent _done = new ManualResetEvent(true);

        public static void Begin(CameraController cc, Vector3 endPos, Vector2 endAngle, float endSize, float seconds)
        {
            _cc = cc;
            _p0 = cc.m_currentPosition; _a0 = cc.m_currentAngle; _s0 = cc.m_currentSize;
            _p1 = endPos; _a1 = endAngle; _s1 = endSize;
            _elapsed = 0f;
            _duration = Mathf.Max(0.01f, seconds);
            _done.Reset();
            _active = true;
        }

        public static void Tick(float dt)
        {
            if (!_active || _cc == null) return;
            _elapsed += dt;
            float t = Mathf.Clamp01(_elapsed / _duration);
            float e = t * t * (3f - 2f * t);   // smoothstep ease-in-out
            Vector3 p = Vector3.Lerp(_p0, _p1, e);
            Vector2 ang = Vector2.Lerp(_a0, _a1, e);
            float s = Mathf.Lerp(_s0, _s1, e);
            _cc.m_currentPosition = p; _cc.m_targetPosition = p;
            _cc.m_currentAngle = ang; _cc.m_targetAngle = ang;
            _cc.m_currentSize = s; _cc.m_targetSize = s;
            if (t >= 1f) { _active = false; _done.Set(); }
        }

        public static bool WaitFor(int ms) { return _done.WaitOne(ms); }
    }
}

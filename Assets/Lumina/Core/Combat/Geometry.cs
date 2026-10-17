using System;
using Lumina.Core.Util;

namespace Lumina.Core.Combat
{
    /// <summary>평면 판정 도형 검사. 대상 반지름(몸집)을 포함해 판정한다.</summary>
    public static class Geometry
    {
        public static bool InCircle(Vec2 center, float radius, Vec2 p, float targetRadius)
        {
            float r = radius + targetRadius;
            return (p - center).SqrLength <= r * r;
        }

        public static bool InCone(Vec2 origin, Vec2 dir, float range, float arcDeg, Vec2 p, float targetRadius)
        {
            Vec2 d = p - origin;
            float dist = d.Length;
            if (dist > range + targetRadius) return false;
            if (dist <= targetRadius + 0.35f) return true;
            if (arcDeg >= 359f) return true;
            float ang = Vec2.AngleBetweenDeg(dir, d);
            // 몸집만큼 각도 여유
            float slack = MathF.Atan2(targetRadius, MathF.Max(dist, 0.01f)) * (180f / MathF.PI);
            return ang <= arcDeg * 0.5f + slack;
        }

        /// <summary>origin에서 dir 방향 [-back, length] 구간, 반폭 halfWidth 직사각형.</summary>
        public static bool InLine(Vec2 origin, Vec2 dir, float length, float back, float halfWidth, Vec2 p, float targetRadius)
        {
            Vec2 n = dir.Normalized;
            Vec2 d = p - origin;
            float along = Vec2.Dot(d, n);
            if (along < -back - targetRadius || along > length + targetRadius) return false;
            float side = MathF.Abs(Vec2.Cross(n, d));
            return side <= halfWidth + targetRadius;
        }

        /// <summary>선분 a→b 와 점 p 사이 최단거리.</summary>
        public static float SegmentDistance(Vec2 a, Vec2 b, Vec2 p)
        {
            Vec2 ab = b - a;
            float len2 = ab.SqrLength;
            if (len2 < 1e-6f) return (p - a).Length;
            float t = Math.Clamp(Vec2.Dot(p - a, ab) / len2, 0f, 1f);
            return (a + ab * t - p).Length;
        }

        public static Vec2 ClampToArena(Vec2 p, float arenaRadius, float bodyRadius)
        {
            float max = arenaRadius - bodyRadius;
            return p.Length > max ? p.Normalized * max : p;
        }
    }
}

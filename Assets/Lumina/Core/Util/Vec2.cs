using System;

namespace Lumina.Core.Util
{
    /// <summary>
    /// 전투 평면 좌표. x = 월드 X(오른쪽), y = 월드 Z(앞쪽). UnityEngine 의존 없이 코어 시뮬레이션에서 사용한다.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x;
        public float y;

        public Vec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 Forward = new Vec2(0f, 1f);

        public float Length => MathF.Sqrt(x * x + y * y);
        public float SqrLength => x * x + y * y;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec2(x / l, y / l) : Zero;
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.x / s, a.y / s);

        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.y * b.y;
        public static float Cross(Vec2 a, Vec2 b) => a.x * b.y - a.y * b.x;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        public static Vec2 FromAngleDeg(float deg)
        {
            float r = deg * (MathF.PI / 180f);
            return new Vec2(MathF.Cos(r), MathF.Sin(r));
        }

        public float AngleDeg => MathF.Atan2(y, x) * (180f / MathF.PI);

        /// <summary>두 벡터 사이 각도(0~180도).</summary>
        public static float AngleBetweenDeg(Vec2 a, Vec2 b)
        {
            Vec2 na = a.Normalized;
            Vec2 nb = b.Normalized;
            float d = Math.Clamp(Dot(na, nb), -1f, 1f);
            return MathF.Acos(d) * (180f / MathF.PI);
        }

        public Vec2 Rotated(float deg)
        {
            float r = deg * (MathF.PI / 180f);
            float c = MathF.Cos(r);
            float s = MathF.Sin(r);
            return new Vec2(x * c - y * s, x * s + y * c);
        }

        public Vec2 ClampLength(float max)
        {
            float l = Length;
            return l > max && l > 1e-6f ? this * (max / l) : this;
        }

        public bool Equals(Vec2 other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public override string ToString() => $"({x:0.00}, {y:0.00})";
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PentaWay
{
    public static class UIStyle
    {
        public static readonly Color Blue = new Color32(0x5B, 0x7C, 0xFA, 0xFF);
        public static readonly Color BlueDark = new Color32(0x45, 0x60, 0xD6, 0xFF);
        public static readonly Color BlueLight = new Color32(0xEC, 0xF0, 0xFF, 0xFF);
        public static readonly Color Green = new Color32(0x2F, 0xB4, 0x86, 0xFF);
        public static readonly Color GreenLight = new Color32(0xE3, 0xF7, 0xEF, 0xFF);
        public static readonly Color Red = new Color32(0xFF, 0x6B, 0x6B, 0xFF);
        public static readonly Color Page = new Color32(0xF6, 0xF7, 0xFC, 0xFF);
        public static readonly Color Card = Color.white;
        public static readonly Color Text = new Color32(0x2D, 0x2A, 0x4A, 0xFF);
        public static readonly Color Muted = new Color32(0x8A, 0x87, 0xA0, 0xFF);
        public static readonly Color Divider = new Color32(0xEC, 0xE8, 0xF2, 0xFF);
        public static readonly Color ToastBg = new Color32(0x2D, 0x2A, 0x4A, 0xE6);

        public static readonly Color[] PastelBg =
        {
            new Color32(0xEC, 0xF0, 0xFF, 0xFF), new Color32(0xE3, 0xF7, 0xEF, 0xFF),
            new Color32(0xFF, 0xEA, 0xDD, 0xFF), new Color32(0xF1, 0xE8, 0xFF, 0xFF)
        };
        public static readonly Color[] PastelFg =
        {
            new Color32(0x5B, 0x7C, 0xFA, 0xFF), new Color32(0x2F, 0xB4, 0x86, 0xFF),
            new Color32(0xF2, 0x8B, 0x4B, 0xFF), new Color32(0x9B, 0x6B, 0xE8, 0xFF)
        };

        const int RoundedSize = 128;
        const float RoundedRadius = 48f;

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Rounded => Get("rounded", () =>
        {
            Vector2 half = new Vector2(RoundedSize, RoundedSize) * 0.5f;
            return Draw(RoundedSize, p => RoundRect(p - half, half, RoundedRadius), new Vector4(RoundedRadius, RoundedRadius, RoundedRadius, RoundedRadius));
        });

        public static Sprite Circle => Get("circle", () => Draw(256, p => (p - new Vector2(128, 128)).magnitude - 124f));
        public static Sprite Ring => Get("ring", () => Draw(256, p => Mathf.Abs((p - new Vector2(128, 128)).magnitude - 114f) - 8f));

        public static Sprite IconMenu => Get("menu", () => Draw(128, p => Min(
            Segment(p, new Vector2(32, 40), new Vector2(96, 40), 6),
            Segment(p, new Vector2(32, 64), new Vector2(96, 64), 6),
            Segment(p, new Vector2(32, 88), new Vector2(96, 88), 6))));

        public static Sprite IconSearch => Get("search", () => Draw(128, p => Mathf.Min(
            Mathf.Abs((p - new Vector2(56, 72)).magnitude - 26f) - 6f,
            Segment(p, new Vector2(76, 52), new Vector2(98, 30), 7))));

        public static Sprite IconClose => Get("close", () => Draw(128, p => Mathf.Min(
            Segment(p, new Vector2(38, 38), new Vector2(90, 90), 7),
            Segment(p, new Vector2(38, 90), new Vector2(90, 38), 7))));

        public static Sprite IconChevronLeft => Get("chevL", () => Draw(128, p => Mathf.Min(
            Segment(p, new Vector2(76, 30), new Vector2(46, 64), 8),
            Segment(p, new Vector2(46, 64), new Vector2(76, 98), 8))));

        public static Sprite IconChevronRight => Get("chevR", () => Draw(128, p => Mathf.Min(
            Segment(p, new Vector2(52, 30), new Vector2(82, 64), 8),
            Segment(p, new Vector2(82, 64), new Vector2(52, 98), 8))));

        public static Sprite IconCheck => Get("check", () => Draw(128, p => Mathf.Min(
            Segment(p, new Vector2(34, 66), new Vector2(56, 44), 8),
            Segment(p, new Vector2(56, 44), new Vector2(96, 86), 8))));

        public static Sprite IconTarget => Get("target", () => Draw(128, p => Mathf.Min(
            (p - new Vector2(64, 64)).magnitude - 18f,
            Mathf.Abs((p - new Vector2(64, 64)).magnitude - 42f) - 7f)));

        public static Sprite IconMap => Get("map", () => Draw(128, p => Min(
            Segment(p, new Vector2(28, 38), new Vector2(28, 98), 5),
            Segment(p, new Vector2(28, 98), new Vector2(52, 88), 5),
            Segment(p, new Vector2(52, 88), new Vector2(76, 98), 5),
            Segment(p, new Vector2(76, 98), new Vector2(100, 88), 5),
            Segment(p, new Vector2(100, 88), new Vector2(100, 28), 5),
            Segment(p, new Vector2(100, 28), new Vector2(76, 38), 5),
            Segment(p, new Vector2(76, 38), new Vector2(52, 28), 5),
            Segment(p, new Vector2(52, 28), new Vector2(28, 38), 5),
            Segment(p, new Vector2(52, 28), new Vector2(52, 88), 5),
            Segment(p, new Vector2(76, 38), new Vector2(76, 98), 5))));

        public static Sprite Arrow => Get("arrow", () => Draw(128, p =>
        {
            float stem = Segment(p, new Vector2(64, 14), new Vector2(64, 70), 14);
            float left = Vector2.Dot(p - new Vector2(64, 118), new Vector2(-0.83f, 0.55f));
            float right = Vector2.Dot(p - new Vector2(64, 118), new Vector2(0.83f, 0.55f));
            float head = Mathf.Max(left, Mathf.Max(right, 60f - p.y));
            return Mathf.Min(stem, head);
        }));

        public static Sprite Dot => Get("dot", () => Draw(128, p => Mathf.Min(
            (p - new Vector2(64, 64)).magnitude - 26f,
            Mathf.Abs((p - new Vector2(64, 64)).magnitude - 51f) - 9f)));

        public static Sprite VerticalFade => Get("vfade", () =>
        {
            const int h = 64;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[4 * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                byte a = (byte)(255f * t * t);
                for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 4, h), new Vector2(0.5f, 0.5f), 100f);
        });

        public static Image Round(Image image, float radius)
        {
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = RoundedRadius / Mathf.Max(1f, radius);
            return image;
        }

        public static void AddShadow(Graphic graphic)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.18f, 0.16f, 0.35f, 0.12f);
            shadow.effectDistance = new Vector2(0f, -8f);
        }

        static Sprite Get(string key, Func<Sprite> create)
        {
            Sprite sprite;
            if (Cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            sprite = create();
            Cache[key] = sprite;
            return sprite;
        }

        static Sprite Draw(int size, Func<Vector2, float> sdf, Vector4 border = default(Vector4))
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - sdf(new Vector2(x + 0.5f, y + 0.5f))) * 255f));
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return (p - (a + ab * t)).magnitude - radius;
        }

        static float RoundRect(Vector2 p, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(radius, radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float Min(params float[] values)
        {
            float m = float.MaxValue;
            foreach (float v in values) m = Mathf.Min(m, v);
            return m;
        }
    }
}

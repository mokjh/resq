using System.Collections.Generic;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Dressing meshes per world and decor kind. Uses the Blender nature kit (<see cref="NatureKit"/>) when its
    /// meshes are present; otherwise builds faceted low-poly placeholders here, so scatter always has something
    /// world-appropriate. Sizes are stylised board millimetres (residents 34 mm, cells 20 mm).
    /// </summary>
    public sealed class DecorPalette
    {
        readonly Dictionary<DecorKind, Mesh[]> _meshes = new Dictionary<DecorKind, Mesh[]>();
        readonly Dictionary<DecorKind, Mesh> _charred = new Dictionary<DecorKind, Mesh>();
        static readonly Dictionary<string, DecorPalette> Cache = new Dictionary<string, DecorPalette>();

        public Mesh[] Get(DecorKind k) => _meshes.TryGetValue(k, out var m) ? m : null;
        public Mesh Charred(DecorKind k) => _charred.TryGetValue(k, out var m) ? m : null;

        public static DecorPalette For(string world)
        {
            world = world ?? "W1";
            if (Cache.TryGetValue(world, out var p)) return p;
            p = new DecorPalette();
            var kit = NatureKit.Load();
            if (kit != null && kit.Fill(world, p._meshes, p._charred) > 0) { Cache[world] = p; return p; }
            p.Placeholders(world);
            Cache[world] = p;
            return p;
        }

        static Color H(int rgb) => TerrainBuilder.Hex(rgb);
        const float Mm = 0.001f;

        void Placeholders(string world)
        {
            var trunk = H(0x6B4A32); var charTrunk = H(0x2B2420);
            Color leafA, leafB, pine, grass, grassTip, rock, rockTop, shrub;
            switch (world)
            {
                case "W2": leafA = H(0xC8782E); leafB = H(0xD9A03C); pine = H(0x2E4A30); grass = H(0xB8A35A); grassTip = H(0xD8C47A); rock = H(0x8A8173); rockTop = H(0xA49C8C); shrub = H(0x6E7A3A); break;
                case "W3": leafA = H(0x8C969C); leafB = H(0xA8B0B4); pine = H(0x2F4A40); grass = H(0xC9D2D8); grassTip = H(0xF4F7FA); rock = H(0x6E767E); rockTop = H(0xF1F4F7); shrub = H(0x5E6E66); break;
                case "W4": leafA = H(0x5E7A4A); leafB = H(0x6E8A55); pine = H(0x3E5A3A); grass = H(0xB9B07A); grassTip = H(0xD8D09A); rock = H(0x8C8577); rockTop = H(0x9C9587); shrub = H(0x5A6E46); break;
                case "W5": leafA = H(0x7C8A5A); leafB = H(0x8E9A66); pine = H(0x4A5A3A); grass = H(0x8A8A55); grassTip = H(0xA8A870); rock = H(0x3A3532); rockTop = H(0x6A625A); shrub = H(0x5E6A3E); break;
                default: leafA = H(0x4C7A5A); leafB = H(0x3B6247); pine = H(0x2F5640); grass = H(0x6E9A58); grassTip = H(0x9CC27A); rock = H(0x9A9284); rockTop = H(0xB0A898); shrub = H(0x456E4C); break;
            }
            bool snow = world == "W3";

            Mesh Broadleaf(int seed, Color a, Color b)
            {
                var k = new MeshKit().Cylinder(Vector3.zero, 1.6f * Mm, 18f * Mm, 6, trunk);
                k.Blob(new Vector3(0, 30f * Mm, 0), new Vector3(12f, 11f, 12f) * Mm, a, a * 0.62f, seed);
                k.Blob(new Vector3(5f * Mm, 24f * Mm, 3f * Mm), new Vector3(8f, 7f, 8f) * Mm, b, b * 0.6f, seed + 3);
                k.Blob(new Vector3(-5f * Mm, 25f * Mm, -4f * Mm), new Vector3(8f, 7f, 8f) * Mm, a, a * 0.6f, seed + 5);
                return k.Build("Broadleaf");
            }
            Mesh Conifer(int seed, float height)
            {
                var tip = snow ? H(0xF4F7FA) : pine * 1.15f;
                var k = new MeshKit().Cylinder(Vector3.zero, 1.3f * Mm, 8f * Mm, 6, trunk);
                float h = height * Mm;
                k.Cone(new Vector3(0, h * 0.14f, 0), 10.5f * Mm, h * 0.42f, 8, pine * 0.85f, snow ? H(0xE9EEF2) : pine);
                k.Cone(new Vector3(0, h * 0.38f, 0), 8f * Mm, h * 0.38f, 8, pine * 0.9f, snow ? H(0xEEF2F5) : pine);
                k.Cone(new Vector3(0, h * 0.62f, 0), 5.5f * Mm, h * 0.38f, 8, pine, tip);
                return k.Build("Conifer");
            }
            Mesh Charred()
            {
                var k = new MeshKit().Cylinder(Vector3.zero, 1.5f * Mm, 26f * Mm, 5, charTrunk);
                k.Segment(new Vector3(0, 18f * Mm, 0), new Vector3(6f, 26f, 2f) * Mm, 0.6f * Mm, charTrunk);
                k.Segment(new Vector3(0, 14f * Mm, 0), new Vector3(-5f, 21f, -3f) * Mm, 0.6f * Mm, charTrunk);
                return k.Build("Charred");
            }
            Mesh Shrub(int seed) => new MeshKit().Blob(new Vector3(0, 5f * Mm, 0), new Vector3(8f, 6f, 8f) * Mm, shrub, shrub * 0.6f, seed).Build("Shrub");
            Mesh Rock(int seed, float r) => new MeshKit().Blob(new Vector3(0, r * 0.35f * Mm, 0), new Vector3(r, r * 0.7f, r * 0.85f) * Mm, rockTop, rock * 0.75f, seed, 0.28f, 6).Build("Rock");
            Mesh Grass(int seed)
            {
                var k = new MeshKit();
                for (int i = 0; i < 4; i++)
                {
                    float a = (seed * 1.7f + i * 1.9f), r = 1.6f * Mm;
                    k.Cone(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), 0.9f * Mm, (4.2f + (i % 2) * 1.6f) * Mm, 4, grass, grassTip);
                }
                return k.Build("Grass");
            }
            Mesh Flower(int seed)
            {
                Color[] petals = { H(0xE8D44D), H(0xE07A9A), H(0xF4F0E6), H(0x9B7FD0) };
                var k = new MeshKit();
                for (int i = 0; i < 5; i++)
                {
                    float a = seed + i * 1.3f, r = (1.2f + i * 0.5f) * Mm;
                    var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                    k.Cone(p, 0.5f * Mm, 3.4f * Mm, 3, grass, grass);
                    k.Blob(p + new Vector3(0, 3.6f * Mm, 0), Vector3.one * 0.9f * Mm, petals[(seed + i) % petals.Length], petals[(seed + i) % petals.Length] * 0.8f, seed + i, 0.1f, 5);
                }
                return k.Build("Flower");
            }
            Mesh Reed(int seed)
            {
                var k = new MeshKit();
                for (int i = 0; i < 5; i++)
                {
                    float a = seed + i * 1.25f, r = 1.4f * Mm;
                    k.Cone(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), 0.6f * Mm, (10f + i % 3 * 1.5f) * Mm, 4, H(0x7A8A4A), H(0xA8A060));
                }
                return k.Build("Reed");
            }
            Mesh Crop(int seed)
            {
                var k = new MeshKit();
                for (int i = -2; i <= 2; i++) k.Blob(new Vector3(i * 3.2f * Mm, 2.2f * Mm, 0), new Vector3(1.6f, 2.4f, 1.6f) * Mm, H(0x9AB04E), H(0x6A7E3A), seed + i, 0.15f, 5);
                return k.Build("Crop");
            }
            Mesh Orchard(int seed)
            {
                var k = new MeshKit().Cylinder(Vector3.zero, 1.2f * Mm, 12f * Mm, 6, trunk);
                k.Blob(new Vector3(0, 19f * Mm, 0), new Vector3(9f, 8f, 9f) * Mm, leafA * 1.05f, leafA * 0.6f, seed);
                Color fruit = world == "W5" ? H(0x3A4A2A) : H(0xD8452E);
                for (int i = 0; i < 4; i++) k.Blob(new Vector3(Mathf.Cos(i * 1.6f) * 7f * Mm, (17f + i % 2 * 3f) * Mm, Mathf.Sin(i * 1.6f) * 7f * Mm), Vector3.one * 1.1f * Mm, fruit, fruit * 0.7f, seed + i, 0.05f, 5);
                return k.Build("Orchard");
            }
            Mesh Drift(int seed)
            {
                var col = world == "W5" ? H(0x6A645E) : H(0xF1F4F7);
                return new MeshKit().Blob(new Vector3(0, 0.6f * Mm, 0), new Vector3(9f, 3.2f, 6f) * Mm, col, col * 0.85f, seed, 0.12f, 7).Build("Drift");
            }
            Mesh Column(int seed)
            {
                var k = new MeshKit();
                for (int i = 0; i < 4; i++)
                {
                    float a = seed + i * 1.6f, r = (i == 0 ? 0 : 3.4f) * Mm;
                    k.Cylinder(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), 2.4f * Mm, (14f + (seed + i) % 4 * 3f) * Mm, 6, H(0x2E2A28));
                }
                return k.Build("Column");
            }
            Mesh Prop(int seed)
            {
                var k = new MeshKit();
                switch (world)
                {
                    case "W4":
                        k.Box(new Vector3(0, 3.5f * Mm, 0), new Vector3(7f, 7f, 7f) * Mm, H(0x9B7A52));
                        k.Cylinder(new Vector3(8f * Mm, 0, 2f * Mm), 2.6f * Mm, 7f * Mm, 8, H(0x5A6E7A));
                        break;
                    case "W2":
                        for (int i = 0; i < 3; i++) k.Segment(new Vector3(-6f, 1.5f + i * 2.2f, i * 0.4f) * Mm, new Vector3(6f, 1.5f + i * 2.2f, i * 0.4f) * Mm, 1.1f * Mm, H(0x7A5A3C));
                        break;
                    default:
                        k.Box(new Vector3(0, 2.5f * Mm, 0), new Vector3(9f, 5f, 4f) * Mm, H(0xD8C47A)); // hay bale
                        break;
                }
                return k.Build("Prop");
            }

            _meshes[DecorKind.Broadleaf] = new[] { Broadleaf(1, leafA, leafB), Broadleaf(7, leafB, leafA), Broadleaf(13, leafA * 1.08f, leafB) };
            _meshes[DecorKind.Conifer] = new[] { Conifer(2, 52f), Conifer(5, 58f), Conifer(9, 46f) };
            _meshes[DecorKind.Shrub] = new[] { Shrub(3), Shrub(11) };
            _meshes[DecorKind.Rock] = new[] { Rock(4, 5f), Rock(8, 7.5f), Rock(15, 3.6f) };
            _meshes[DecorKind.Grass] = new[] { Grass(1), Grass(4) };
            _meshes[DecorKind.Flower] = new[] { Flower(2), Flower(5) };
            _meshes[DecorKind.Reed] = new[] { Reed(3) };
            _meshes[DecorKind.Crop] = new[] { Crop(6) };
            _meshes[DecorKind.Orchard] = new[] { Orchard(4), Orchard(9) };
            _meshes[DecorKind.Drift] = new[] { Drift(1), Drift(6) };
            _meshes[DecorKind.Column] = new[] { Column(3), Column(7) };
            _meshes[DecorKind.Prop] = new[] { Prop(1) };
            var charred = Charred();
            _charred[DecorKind.Broadleaf] = charred;
            _charred[DecorKind.Conifer] = charred;
            _charred[DecorKind.Orchard] = charred;
            _charred[DecorKind.Shrub] = Rock(21, 3f);
            _charred[DecorKind.Grass] = new MeshKit().Blob(Vector3.zero, new Vector3(3f, 0.6f, 3f) * Mm, H(0x2E2A26), H(0x1E1A18), 2, 0.1f, 5).Build("Ash");
            _charred[DecorKind.Reed] = _charred[DecorKind.Grass];
        }
    }
}

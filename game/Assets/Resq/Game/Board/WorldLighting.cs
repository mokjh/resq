using UnityEngine;
using UnityEngine.Rendering;

namespace Resq.Game
{
    /// <summary>
    /// Golden-hour light per world (art direction 23 Oct, concept frames): a low warm sun from the player's
    /// front-left, so faces and facades toward the player are lit and shadows run long across the board, plus a
    /// trilight ambient tuned per world (cool sky, warm horizon, ground bounce: snow bright in Frostline, lava-red in
    /// Cinder Reach). The sun is placed relative to the board, so the look holds wherever the diorama sits in the
    /// room. Presentation only.
    /// </summary>
    public static class WorldLighting
    {
        public struct Preset
        {
            public Color Sun, Sky, Equator, Ground;
            public float Intensity, Elevation, Azimuth, ShadowStrength;
        }

        public static Preset For(string world)
        {
            Color H(int rgb) => TerrainBuilder.Hex(rgb);
            switch (world)
            {
                case "W2": // Emberwood: smoky orange low sun
                    return new Preset { Sun = H(0xFFC694), Intensity = 1.25f, Elevation = 27f, Azimuth = 58f, ShadowStrength = 0.85f,
                                        Sky = H(0x8A8C98), Equator = H(0x988270), Ground = H(0x403428) };
                case "W3": // Frostline: pale gold sun, bright snow bounce, blue sky
                    return new Preset { Sun = H(0xFFE2BC), Intensity = 1.22f, Elevation = 30f, Azimuth = 50f, ShadowStrength = 0.75f,
                                        Sky = H(0xA2B8DA), Equator = H(0xB8B0AE), Ground = H(0x7A8088) };
                case "W4": // Tidemark: rose-gold sunset over the sea
                    return new Preset { Sun = H(0xFFC49C), Intensity = 1.3f, Elevation = 24f, Azimuth = 64f, ShadowStrength = 0.82f,
                                        Sky = H(0x86A2C4), Equator = H(0xB48E84), Ground = H(0x3A4346) };
                case "W5": // Cinder Reach: amber sun, red lava bounce from below
                    return new Preset { Sun = H(0xFFC08E), Intensity = 1.3f, Elevation = 28f, Azimuth = 55f, ShadowStrength = 0.82f,
                                        Sky = H(0x7E7680), Equator = H(0x9C7662), Ground = H(0x58342A) };
                default:   // Alder Valley: classic warm gold
                    return new Preset { Sun = H(0xFFCF92), Intensity = 1.35f, Elevation = 30f, Azimuth = 55f, ShadowStrength = 0.85f,
                                        Sky = H(0x8496B8), Equator = H(0xA08670), Ground = H(0x433A2E) };
            }
        }

        /// <summary>Applies the world's preset to the scene's sun and ambient, oriented to <paramref name="board"/>.</summary>
        public static void Apply(string world, Transform board)
        {
            var p = For(world);
            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                // from the player's front-left (board -Z, -X), pointing across the board
                var local = Quaternion.Euler(p.Elevation, p.Azimuth, 0f);
                sun.transform.rotation = (board != null ? Quaternion.Euler(0, board.eulerAngles.y, 0) : Quaternion.identity) * local;
                sun.color = p.Sun;
                sun.intensity = p.Intensity;
                sun.shadowStrength = p.ShadowStrength;
                RenderSettings.sun = sun;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.Sky;
            RenderSettings.ambientEquatorColor = p.Equator;
            RenderSettings.ambientGroundColor = p.Ground;
            // SH ambient for the RESQ shaders (SampleSH) comes from the ambient probe: rebuild it from the trilight
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(p.Equator.linear);
            sh.AddDirectionalLight(Vector3.up, (p.Sky.linear - p.Equator.linear) * 0.5f, 1f);
            sh.AddDirectionalLight(Vector3.down, (p.Ground.linear - p.Equator.linear) * 0.5f, 1f);
            RenderSettings.ambientProbe = sh;
        }
    }
}

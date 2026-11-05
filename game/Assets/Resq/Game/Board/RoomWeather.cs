using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// The real room takes on the mission's weather (BUILD_PLAN decision 7, P4.2). Rain or snow falls from the player's
    /// actual ceiling onto the valley, or embers rise to it, with a storm cloud gathered under the ceiling above the board
    /// in the wet worlds. The room itself dims and takes the world's colour as the run goes on, flashes with lightning,
    /// and clears when everyone is safe. The XR layer supplies the ceiling height and applies <see cref="RoomWeight"/>
    /// to passthrough (RoomMoodXr); without a room the ceiling is assumed 1.5 m above the board.
    /// Presentation only: the simulation never reads any of this. Reduced effects: fewer particles, no flashes.
    /// </summary>
    public sealed class RoomWeather : MonoBehaviour
    {
        public enum Fall { Rain, Snow, Embers }

        public struct Preset
        {
            public Fall Kind;
            public float Rate, Speed, Size, Stretch, Wind;
            public Color Color;
            /// <summary>Seconds between lightning strikes; 0 = none in this world.</summary>
            public float StrikeMin, StrikeMax;
            public bool Cloud;
            /// <summary>What the real room looks like at full weather: colour filter, brightness and saturation.</summary>
            public Color RoomTint;
            public float RoomExposure, RoomSaturation;
        }

        public static Preset For(string world)
        {
            switch (world)
            {
                case "W2":  // Emberwood: sparks rise from the forest, the room goes smoky orange
                    return new Preset { Kind = Fall.Embers, Rate = 16, Speed = 0.3f, Size = 0.0035f, Wind = 0.05f, Color = new Color(1f, 0.55f, 0.18f, 0.95f),
                                        RoomTint = new Color(1f, 0.74f, 0.5f), RoomExposure = 0.6f, RoomSaturation = 0.7f };
                case "W3":  // Frostline: snow from the ceiling, a cold pale room
                    return new Preset { Kind = Fall.Snow, Rate = 22, Speed = 0.2f, Size = 0.0045f, Wind = 0.05f, Color = new Color(1f, 1f, 1f, 0.85f),
                                        RoomTint = new Color(0.82f, 0.91f, 1f), RoomExposure = 0.68f, RoomSaturation = 0.45f };
                case "W4":  // Tidemark: a slanting squall at dusk
                    return new Preset { Kind = Fall.Rain, Rate = 120, Speed = 2f, Size = 0.0015f, Stretch = 0.018f, Wind = 0.35f, Color = new Color(0.82f, 0.9f, 1f, 0.45f),
                                        StrikeMin = 16f, StrikeMax = 34f, Cloud = true,
                                        RoomTint = new Color(1f, 0.8f, 0.76f), RoomExposure = 0.58f, RoomSaturation = 0.7f };
                case "W5":  // Cinder Reach: embers and a red room
                    return new Preset { Kind = Fall.Embers, Rate = 22, Speed = 0.42f, Size = 0.003f, Wind = 0.08f, Color = new Color(1f, 0.36f, 0.1f, 0.95f),
                                        RoomTint = new Color(1f, 0.58f, 0.44f), RoomExposure = 0.46f, RoomSaturation = 0.6f };
                default:    // Alder Valley: the storm behind the flood
                    return new Preset { Kind = Fall.Rain, Rate = 200, Speed = 2.3f, Size = 0.0015f, Stretch = 0.02f, Wind = 0.08f, Color = new Color(0.8f, 0.89f, 1f, 0.45f),
                                        StrikeMin = 11f, StrikeMax = 26f, Cloud = true,
                                        RoomTint = new Color(0.78f, 0.86f, 1f), RoomExposure = 0.46f, RoomSaturation = 0.55f };
            }
        }

        public SimDriver Driver;
        public BoardInteraction Interaction;

        /// <summary>The real ceiling's world height, set by the XR layer; NaN while unknown.</summary>
        public static float CeilingWorldY = float.NaN;
        public const float AssumedCeiling = 1.5f, MinCeiling = 0.6f, MaxCeiling = 3.5f;
        /// <summary>0 = the room as it is, 1 = the world's full filter. Lightning drops it for a moment, so the real room flashes.</summary>
        public static float RoomWeight { get; private set; }
        /// <summary>The world whose room filter applies.</summary>
        public static string RoomWorld { get; private set; } = "W1";
        /// <summary>A lightning strike reached the player's ears (a beat after the flash).</summary>
        public static event System.Action Thunder;

        // read-only views for tests and diagnostics
        public ParticleSystem Particles => _ps;
        public float Level { get; private set; }
        public float Flash { get; private set; }
        public float CeilingAboveBoard => _top;
        public Fall Kind => _preset.Kind;
        public bool CloudVisible => _cloud != null && _cloud.activeSelf;

        ParticleSystem _ps;
        GameObject _cloud;
        Material _mat;
        Preset _preset;
        string _built;
        float _top, _warnUntil, _nextStrike, _strikeAt = -100f, _thunderAt = -1f, _nextCeilingCheck;
        bool _sunTouched;

        void OnEnable() { if (Driver != null) { Driver.OnRunLoaded += OnRunLoaded; Driver.OnSimEvent += OnSimEvent; } }
        void OnDisable()
        {
            if (Driver != null) { Driver.OnRunLoaded -= OnRunLoaded; Driver.OnSimEvent -= OnSimEvent; }
            RoomWeight = 0f;
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }

        void OnRunLoaded()
        {
            _warnUntil = 0f;
            Configure();
            _nextStrike = Time.time + Random.Range(5f, 10f);
        }

        void OnSimEvent(SimEvent e) { if (e.Kind == SimEventKind.Warning) _warnUntil = Time.time + 8f; }

        float Ceiling()
        {
            float above = float.IsNaN(CeilingWorldY) ? AssumedCeiling : CeilingWorldY - transform.position.y;
            return above >= MinCeiling && above <= MaxCeiling ? above : AssumedCeiling;   // a bad reading must not fill the room
        }

        /// <summary>Rebuilds the emitter when the world or the ceiling has changed.</summary>
        void Configure()
        {
            var d = Driver != null ? Driver.Def : null;
            if (d == null) return;
            string world = d.WorldId ?? "W1";
            float top = Ceiling();
            string key = world + "|" + BoardSpace.Extent(d) + "|" + top.ToString("F2");
            if (key == _built) return;
            _built = key;
            _top = top;
            _preset = For(world);
            RoomWorld = world;
            BuildParticles(BoardSpace.Extent(d));
            BuildCloud(BoardSpace.Extent(d));
        }

        void BuildParticles(Vector2 extent)
        {
            if (_mat == null) { var sh = Shader.Find("Resq/Weather"); if (sh != null) _mat = new Material(sh) { name = "RoomWeather" }; }
            if (_ps != null) Destroy(_ps.gameObject);
            var go = new GameObject("Weather");
            go.SetActive(false);   // configure before it first simulates
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            var p = _preset;
            bool rising = p.Kind == Fall.Embers;
            const float Floor = 0.03f;
            float span = Mathf.Max(0.2f, _top - Floor);
            float life = Mathf.Min(span / p.Speed, rising ? 7f : 12f);

            var main = _ps.main;
            main.playOnAwake = true; main.loop = true; main.prewarm = true; main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f;
            main.startLifetime = rising ? new ParticleSystem.MinMaxCurve(life * 0.6f, life) : new ParticleSystem.MinMaxCurve(life);
            main.startSize = new ParticleSystem.MinMaxCurve(p.Size * 0.8f, p.Size * 1.2f);
            main.startColor = p.Color;
            main.maxParticles = 220;

            var em = _ps.emission;
            em.rateOverTime = p.Rate;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(extent.x + 0.06f, 0.001f, extent.y + 0.06f);
            shape.position = new Vector3(0f, rising ? Floor : _top - 0.02f, 0f);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            float s = rising ? p.Speed : -p.Speed;
            vel.y = new ParticleSystem.MinMaxCurve(s * 0.9f, s * 1.05f);
            vel.x = new ParticleSystem.MinMaxCurve(p.Kind == Fall.Rain ? p.Wind : -p.Wind, p.Wind);
            vel.z = new ParticleSystem.MinMaxCurve(p.Kind == Fall.Rain ? 0f : -p.Wind, p.Kind == Fall.Rain ? 0f : p.Wind);

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, rising ? 0.55f : 0.92f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            if (p.Kind == Fall.Rain) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = p.Stretch; r.lengthScale = 1f; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            go.SetActive(true);
        }

        void BuildCloud(Vector2 extent)
        {
            if (_cloud != null) { var old = _cloud.GetComponent<MeshFilter>().sharedMesh; Destroy(_cloud); if (old != null) Destroy(old); _cloud = null; }
            if (!_preset.Cloud) return;
            _cloud = new GameObject("StormCloud");
            _cloud.transform.SetParent(transform, false);
            _cloud.transform.localPosition = new Vector3(0f, _top - 0.03f, 0f);
            _cloud.AddComponent<MeshFilter>().sharedMesh = PuffMesh(extent);
            var r = _cloud.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>A few overlapping soft discs, flat under the ceiling: a dark shape gathering over the board.</summary>
        static Mesh PuffMesh(Vector2 extent)
        {
            var puffs = new[] { new Vector4(0f, 0f, 0.62f, 0.62f), new Vector4(-0.3f, 0.1f, 0.42f, 0.5f), new Vector4(0.32f, -0.08f, 0.44f, 0.5f),
                                new Vector4(0.08f, 0.28f, 0.36f, 0.42f), new Vector4(-0.12f, -0.26f, 0.38f, 0.42f) };
            var v = new Vector3[puffs.Length * 4]; var uv = new Vector2[v.Length]; var c = new Color[v.Length]; var t = new int[puffs.Length * 6];
            float wx = extent.x, wz = extent.y;
            for (int i = 0; i < puffs.Length; i++)
            {
                var pf = puffs[i];
                var centre = new Vector3(pf.x * wx, -0.006f * i, pf.y * wz);
                float rx = pf.z * wx, rz = pf.w * wz;
                int k = i * 4;
                v[k] = centre + new Vector3(-rx, 0, -rz); v[k + 1] = centre + new Vector3(-rx, 0, rz); v[k + 2] = centre + new Vector3(rx, 0, rz); v[k + 3] = centre + new Vector3(rx, 0, -rz);
                uv[k] = new Vector2(0, 0); uv[k + 1] = new Vector2(0, 1); uv[k + 2] = new Vector2(1, 1); uv[k + 3] = new Vector2(1, 0);
                for (int j = 0; j < 4; j++) c[k + j] = new Color(0.03f, 0.035f, 0.05f, 0.5f);
                int n = i * 6;
                t[n] = k; t[n + 1] = k + 1; t[n + 2] = k + 2; t[n + 3] = k; t[n + 4] = k + 2; t[n + 5] = k + 3;
            }
            var m = new Mesh { name = "StormCloud", vertices = v, uv = uv, colors = c, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        float TargetLevel()
        {
            var sim = Driver != null ? Driver.Sim : null;
            if (sim == null) return 0f;
            if (sim.IsOver && sim.S.C.Success) return 0f;   // everyone is safe: the storm passes
            bool menus = Interaction != null && Interaction.MenuOnly;
            float t = menus ? 0.35f : Driver.Paused ? 0.6f : 0.8f;
            if (!menus && Time.time < _warnUntil) t = 1f;
            return Comfort.ReducedEffects ? Mathf.Min(t, 0.5f) : t;
        }

        /// <summary>A lightning strike now, if this world has them and effects allow. False when it can't flash.</summary>
        public bool Strike()
        {
            if (_preset.StrikeMax <= 0f || Comfort.ReducedEffects) return false;
            _strikeAt = Time.time;
            _thunderAt = _strikeAt + Random.Range(0.5f, 1.3f);
            _nextStrike = _strikeAt + Random.Range(_preset.StrikeMin, _preset.StrikeMax);
            return true;
        }

        /// <summary>A double stroke: a bright flash, a dip, a second flash, then a quick fade.</summary>
        static float Envelope(float t)
        {
            if (t < 0f) return 0f;
            if (t < 0.05f) return 1f;
            if (t < 0.11f) return 0.25f;
            if (t < 0.17f) return 0.8f;
            return Mathf.Clamp01(0.8f * Mathf.Exp(-(t - 0.17f) * 8f) - 0.01f);
        }

        void LateUpdate()
        {
            if (Time.time >= _nextCeilingCheck)
            {
                _nextCeilingCheck = Time.time + 1f;
                if (Driver != null && Driver.Def != null) Configure();   // the room's scene data can arrive after the first run loads
            }
            Level = Mathf.MoveTowards(Level, TargetLevel(), Time.deltaTime / 2.5f);
            if (_ps != null) { var em = _ps.emission; em.rateOverTimeMultiplier = _preset.Rate * Level; }
            if (_cloud != null)
            {
                bool show = Level > 0.05f;
                if (_cloud.activeSelf != show) _cloud.SetActive(show);
            }

            bool live = Driver != null && Driver.Sim != null && !Driver.Paused && !Driver.Sim.IsOver && !(Interaction != null && Interaction.MenuOnly);
            if (live && Level > 0.5f && _preset.StrikeMax > 0f && Time.time >= _nextStrike) Strike();
            if (_thunderAt > 0f && Time.time >= _thunderAt) { _thunderAt = -1f; Thunder?.Invoke(); }

            Flash = Comfort.ReducedEffects ? 0f : Envelope(Time.time - _strikeAt);
            RoomWeight = Level * (1f - Flash);
            var sun = RenderSettings.sun;
            if (sun != null && (Flash > 0f || _sunTouched))
            {
                sun.intensity = WorldLighting.For(RoomWorld).Intensity * (1f + 1.4f * Flash);
                _sunTouched = Flash > 0f;
            }
        }
    }
}

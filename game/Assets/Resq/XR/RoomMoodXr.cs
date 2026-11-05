using Meta.XR.MRUtilityKit;
using Resq.Game;
using UnityEngine;

namespace Resq.XR
{
    /// <summary>
    /// Carries RoomWeather into the real room: reads the ceiling height from MRUK and grades passthrough with one colour
    /// look-up table per world, whose weight follows RoomWeather.RoomWeight (the room dims and takes the world's colour,
    /// lightning flashes it back for an instant, and it clears when everyone is safe). Changing the weight is cheap, so the
    /// table is only rebuilt when the world changes. Presentation only.
    /// </summary>
    public sealed class RoomMoodXr : MonoBehaviour
    {
        const int Resolution = 16;
        OVRPassthroughLayer _layer;
        OVRPassthroughColorLut _lut;
        string _world;
        float _applied = -1f, _nextCeiling;

        void OnDestroy()
        {
            RoomWeather.CeilingWorldY = float.NaN;
            if (_layer != null) _layer.DisableColorMap();
            _lut?.Dispose();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= _nextCeiling) { _nextCeiling = Time.unscaledTime + 1f; ReadCeiling(); }
            if (_layer == null) { _layer = FindFirstObjectByType<OVRPassthroughLayer>(); if (_layer == null) return; }
            if (!OVRManager.IsInsightPassthroughInitialized()) return;
            if (_world != RoomWeather.RoomWorld || _lut == null) { Build(RoomWeather.RoomWorld); _applied = -1f; }
            if (_lut == null || !_lut.IsValid) return;
            float weight = RoomWeather.RoomWeight;
            if (Mathf.Abs(weight - _applied) < 0.005f) return;
            _layer.SetColorLut(_lut, weight);
            _applied = weight;
        }

        void Build(string world)
        {
            _world = world;
            var p = RoomWeather.For(world);
            var old = _lut;
            _lut = new OVRPassthroughColorLut(Table(p), OVRPassthroughColorLut.ColorChannels.Rgb);
            old?.Dispose();
        }

        /// <summary>The graded room: desaturated, filtered and darkened. Red varies fastest, blue slowest.</summary>
        static Color32[] Table(RoomWeather.Preset p)
        {
            var table = new Color32[Resolution * Resolution * Resolution];
            int i = 0;
            float step = 1f / (Resolution - 1);
            for (int b = 0; b < Resolution; b++)
                for (int g = 0; g < Resolution; g++)
                    for (int r = 0; r < Resolution; r++)
                    {
                        float fr = r * step, fg = g * step, fb = b * step;
                        float luma = 0.2126f * fr + 0.7152f * fg + 0.0722f * fb;
                        table[i++] = new Color(
                            Mathf.Lerp(luma, fr, p.RoomSaturation) * p.RoomTint.r * p.RoomExposure,
                            Mathf.Lerp(luma, fg, p.RoomSaturation) * p.RoomTint.g * p.RoomExposure,
                            Mathf.Lerp(luma, fb, p.RoomSaturation) * p.RoomTint.b * p.RoomExposure, 1f);
                    }
            return table;
        }

        static void ReadCeiling()
        {
            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null || room.CeilingAnchors == null || room.CeilingAnchors.Count == 0) { RoomWeather.CeilingWorldY = float.NaN; return; }
            RoomWeather.CeilingWorldY = room.CeilingAnchors[0].transform.position.y;
        }
    }
}
